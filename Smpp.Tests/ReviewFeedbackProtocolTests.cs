using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;
using ClientPeer = Smpp.Tests.SessionRegressionTests.ClientPeer;
using ServerHost = Smpp.Tests.ServerSessionRegressionTests.ServerHost;

namespace Smpp.Tests
{
    public partial class ReviewFeedbackRegressionTests
    {
        private static async Task AssertPeerClosed(NetworkStream stream)
        {
            try
            {
                Assert.That(await stream.ReadAsync(new byte[1]).AsTask().WaitAsync(TimeSpan.FromSeconds(2)), Is.Zero);
            }
            catch (IOException ex) when (ex.InnerException is SocketException socket &&
                (socket.SocketErrorCode == SocketError.ConnectionReset || socket.SocketErrorCode == SocketError.ConnectionAborted))
            {
                // Windows may reset a socket when cancellation closes a read with bytes in flight.
            }
        }

        private static string MalformedRequest(string command, string failure)
        {
            uint code = command == "submit" ? 4u : command == "deliver" ? 5u : 0x103u;
            if (failure == "mandatory") return Frame(code);
            if (failure == "duplicate-tlv" || failure == "truncated-tlv")
            {
                string valid = Encode(command, "one", mode: 2)[0];
                return Frame(code, valid.Substring(32) + (failure == "duplicate-tlv" ? "0424000141" : "1234000541"));
            }
            if (command == "data")
                return DataFrame("FF", "unicode", udhi: false);
            string body = "000000310000003200" + "00000000000000" +
                (failure == "unicode" ? "08" : "00") + "00" + (failure == "length" ? "02" : "01") + "FF";
            return Frame(code, body);
        }

        [Test]
        public void MalformedRequestsGetOneErrorResponseAndFollowingTrafficStillWorks(
            [Values("submit", "deliver", "data")] string command,
            [Values("mandatory", "duplicate-tlv", "truncated-tlv", "length", "unicode")] string failure)
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), MalformedRequest(command, failure) + Encode("submit", "good")[0]);
            Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo(Frame(0x80000000, status: 1)));
            Assert.That(ReadPdu(peer.Peer.GetStream()).Substring(8, 16), Is.EqualTo("8000000400000000"));
            WaitUntil(() => messages.Count == 1, "The complete frame after a malformed body must still be handled");
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { "good" }));
            Assert.That(peer.Client.connected, Is.True);
        }

        [TestCase("00000000")]
        [TestCase("0000000F")]
        [TestCase("FFFFFFFF")]
        public void BadLengthsCloseTheStreamAndCannotConsumeLaterAcknowledgements(string invalid)
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), invalid + Encode("deliver", "must be discarded")[0]);
            peer.Accept();
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), Encode("deliver", "new session")[0]);
            ReadPdu(peer.Peer.GetStream());
            WaitUntil(() => messages.Count == 1, "The replacement session must frame from its own boundary");
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { "new session" }));
        }

        private sealed class DelayedConnectClient : Client
        {
            internal readonly TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int Attempts;
            internal int Retries;
            internal bool HonorCancellation = true;
            internal DelayedConnectClient() : base("delayed-" + Guid.NewGuid(), new GateEvents()) { }
            protected override async ValueTask ConnectTransportAsync(TcpClient transport, CancellationToken cancellation)
            {
                Interlocked.Increment(ref Attempts);
                Started.TrySetResult();
                if (HonorCancellation) await Release.Task.WaitAsync(cancellation);
                else await Release.Task;
            }
            protected override void ScheduleReconnect(int delayMilliseconds) => Interlocked.Increment(ref Retries);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PendingConnectDoesNotBlockSendingOrQuitAndCannotInstallAfterQuit(bool honorCancellation)
        {
            var client = new DelayedConnectClient { HonorCancellation = honorCancellation };
            try
            {
                var connect = client.ConnectAsync();
                await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(client.ConnectAsync(), Is.SameAs(connect), "Concurrent callers share the attempt");
                Assert.That(await Task.Run(() => client.SubmitSm(1, "1", "2", "early")).WaitAsync(TimeSpan.FromSeconds(2)), Is.EqualTo(1));
                await Task.Run(client.Quit).WaitAsync(TimeSpan.FromSeconds(2));
                client.Release.SetResult();
                Assert.That(await connect.WaitAsync(TimeSpan.FromSeconds(2)), Is.False);
                Assert.That(client.Attempts, Is.EqualTo(1));
                Assert.That(client.Retries, Is.Zero);
                Assert.That(client.connected, Is.False);
                Assert.That(client.Connect(), Is.False);
            }
            finally { client.Release.TrySetResult(); client.Quit(); }
        }

        [Test]
        public async Task ConnectTimeoutCancelsTheAttemptAndSchedulesOnlyOneRetry()
        {
            var client = new DelayedConnectClient { timeout = 60 };
            try
            {
                Assert.That(await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(2)), Is.False);
                Assert.That(client.Retries, Is.EqualTo(1));
                Assert.That(client.connected, Is.False);
            }
            finally { client.Quit(); }
        }

        [TestCase(1)]
        [TestCase(3)]
        public void LateMessageAckExpiresOnlyThatMessageAndKeepsOtherSubmissionsAndKeepalives(int count)
        {
            using var pair = new SocketPair();
            var statuses = new List<(int, Common.MessageStatus)>();
            var events = new GateEvents();
            events.MessageStatusEvent += (id, status, _) => statuses.Add((id, status));
            var connection = new ProbeConnection(events) { timeout = 60000 };
            try
            {
                connection.Attach(pair.Local);
                connection.TrackParts(10, count, 1, DateTime.UtcNow);
                connection.AgeSubmission(10);
                connection.Track(20, 2, DateTime.UtcNow);
                connection.Queue(30, DateTime.UtcNow);
                connection.Tick();
                Assert.That(connection.connected, Is.True);
                Assert.That(connection.Pending, Is.EqualTo(1));
                Assert.That(connection.Reconnects, Is.Zero);
                for (uint i = 10; i < 10 + count; i++)
                    connection.MessageHandler(Frame(0x80000004, sequence: i));
                connection.MessageHandler(Frame(0x80000004, sequence: 20) + Frame(0x80000015, sequence: 30));
                connection.Tick();
                Assert.That(statuses, Is.EqualTo(new[] { (1, Common.MessageStatus.timeout), (2, Common.MessageStatus.sent) }));
                Assert.That(connection.connected, Is.True);
                Assert.That(connection.TimerEnabled, Is.True);
            }
            finally { connection.Quit(); }
        }

        [TestCase("message")]
        [TestCase("status")]
        [TestCase("receipt")]
        [TestCase("disconnect")]
        [TestCase("log")]
        public async Task ApplicationCallbacksCanWaitForAnotherThreadToUseTheConnection(string kind)
        {
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new GateEvents();
            var connection = new ProbeConnection(events);
            int calls = 0;
            void Callback()
            {
                if (Interlocked.Increment(ref calls) != 1) return;
                bool unlocked = !connection.SessionLockHeld;
                var work = Task.Run(() => connection.SubmitSm(100, "1", "2", "reply"));
                completed.TrySetResult(unlocked && work.Wait(TimeSpan.FromSeconds(2)));
            }
            if (kind == "message") events.NewMessageEvent += (_, _, _, _, _, _, _) => Callback();
            if (kind == "status" || kind == "disconnect") events.MessageStatusEvent += (_, _, _) => Callback();
            if (kind == "receipt") events.MessageDeliveryReportEvent += (_, _) => Callback();
            if (kind == "log") events.ChannelEvent += (_, _, _) => Callback();
            try
            {
                if (kind == "status" || kind == "disconnect")
                {
                    connection.Track(10, 1, DateTime.UtcNow);
                    if (kind == "status") ((Connection)connection).MessageHandler(Frame(0x80000004, sequence: 10));
                    else connection.Drop();
                }
                else if (kind == "receipt")
                {
                    var receipt = new Smpp.Requests.DeliverSm(events)
                    {
                        Sender = "1", Recipient = "2", Body = "id:42 stat:DELIVRD", BodyFormat = "ascii", IsDeliveryReport = true
                    };
                    ((Connection)connection).MessageHandler(receipt.EncodeM()[0]);
                }
                else ((Connection)connection).MessageHandler(Encode("submit", "trigger")[0]);
                Assert.That(await completed.Task.WaitAsync(TimeSpan.FromSeconds(4)), Is.True, "Callbacks must run outside SessionLock");
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void SlowAndThrowingCallbacksDoNotBlockProtocolResponsesOrLaterEvents()
        {
            using var started = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) =>
            {
                if (body == "slow")
                {
                    started.Set();
                    release.Wait(TimeSpan.FromSeconds(5));
                    throw new InvalidOperationException("subscriber failed");
                }
                messages.Enqueue(body);
            };
            events.Event += (_, _) => throw new InvalidOperationException("error subscriber also failed");
            using var peer = new ClientPeer(events: events);
            try
            {
                peer.BindSuccessfully();
                WritePdu(peer.Peer.GetStream(), Encode("deliver", "slow")[0]);
                ReadPdu(peer.Peer.GetStream());
                Assert.That(started.Wait(TimeSpan.FromSeconds(2)), Is.True);
                WritePdu(peer.Peer.GetStream(), Frame(0x15) + Encode("deliver", "next")[0]);
                Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo(Frame(0x80000015)));
                Assert.That(ReadPdu(peer.Peer.GetStream()).Substring(8, 8), Is.EqualTo("80000005"));
                release.Set();
                WaitUntil(() => messages.Count == 1, "Later callbacks must still be dispatched");
                Assert.That(messages.ToArray(), Is.EqualTo(new[] { "next" }));
            }
            finally { release.Set(); }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(4)]
        public async Task UnauthenticatedBindHasAnOverallDeadlineEvenWhileBytesTrickleIn(int prefixLength)
        {
            using var pair = new SocketPair();
            var stream = pair.Remote.GetStream();
            byte[] bind = Convert.FromHexString(Frame(9, Common.StringToHex("username") + "00000034000000"));
            await stream.WriteAsync(bind, 0, prefixLength);
            var watch = Stopwatch.StartNew();
            var handshake = Server.CreateConnection(null, pair.Local, CancellationToken.None, 250);
            var writer = Task.Run(async () =>
            {
                try
                {
                    for (int i = prefixLength; i < bind.Length; i++)
                    {
                        await Task.Delay(60);
                        await stream.WriteAsync(bind, i, 1);
                    }
                }
                catch (IOException) { }
            });
            await handshake.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)), "The deadline spans all reads");
            await AssertPeerClosed(stream);
            await writer.WaitAsync(TimeSpan.FromSeconds(3));
        }

        [Test]
        public async Task SlowUnauthenticatedPeersDoNotBlockBindsAndStopClosesThem()
        {
            using var host = new ServerHost();
            var peers = new List<TcpClient>();
            try
            {
                for (int i = 0; i < 32; i++)
                {
                    var peer = new TcpClient();
                    peers.Add(peer);
                    await peer.ConnectAsync("127.0.0.1", host.Port);
                    await peer.GetStream().WriteAsync(new byte[] { 0 });
                }
                using var bound = host.Bind(9);
                Server.Stop();
                foreach (var peer in peers)
                    await AssertPeerClosed(peer.GetStream());
            }
            finally { foreach (var peer in peers) peer.Dispose(); }
        }

        [Test]
        public void LongWapPushMustUsePayloadAndNeverEmitConflictingHeaders(
            [Values("submit", "deliver")] string command, [Values(false, true)] bool url)
        {
            string body = url ? "https://example.test/" + new string('a', 180) : "0605040B8423F0" + new string('A', 280);
            Assert.Throws<ArgumentException>(() => Encode(command, body, "wap_push"));
            string frame = Encode(command, body, "wap_push", mode: 2).Single();
            var decoded = Decode(command, frame);
            Assert.That(decoded.Body, Does.StartWith("0605040B8423F0"));
            Assert.That(decoded.Multipart, Is.Null);
            Assert.That(decoded.Body, Does.Not.Contain("050003"));
        }

        [Test]
        public void DataSmReassemblesReorderedDuplicatesAndSeparatesRecipientsAndCommands()
        {
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            try
            {
                string a = new string('a', 180), b = new string('b', 180), c = new string('c', 180);
                var first = IncomingParts("data", a);
                var second = IncomingParts("data", b, "3");
                var third = IncomingParts("submit", c);
                connection.MessageHandler(first[1] + second[0] + third[0] + first[1]);
                Assert.That(messages, Is.Empty);
                connection.MessageHandler(first[0] + second[1] + third[1]);
                Assert.That(messages, Is.EqualTo(new[] { a, b, c }));
                Assert.That(connection.MultipartCount, Is.Zero);
            }
            finally { connection.Quit(); }
        }

        [TestCase(0x102u)]
        [TestCase(0xBu)]
        public void NotificationsHaveNoReplyButUnsupportedRequestsStillGetANack(uint command)
        {
            using var peer = new ClientPeer();
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), Frame(command) + Frame(0x765, sequence: 43) + Frame(0x15, sequence: 44));
            Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo(Frame(0x80000000, sequence: 43, status: 3)));
            Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo(Frame(0x80000015, sequence: 44)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SequenceReservationSkipsOccupiedMessageAndCommandRangesAfterWrap(bool multipart)
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection { multypart_delay = 0 };
            try
            {
                connection.Attach(pair.Local);
                connection.Track(1, 1, DateTime.MaxValue);
                connection.Queue(2, DateTime.UtcNow);
                connection.Track(4, 2, DateTime.MaxValue);
                connection.Queue(0x7FFFFFFF, DateTime.UtcNow);
                connection.SetNextSequence(0x7FFFFFFF);
                Assert.That(connection.SubmitSm(3, "1", "2", multipart ? new string('a', 307) : "one"), Is.Zero);
                uint first = multipart ? 5u : 3u;
                for (uint i = 0; i < (multipart ? 3u : 1u); i++)
                    Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16), Is.EqualTo(first + i));
                Assert.That(connection.SubmitSm(4, "1", "2", "next"), Is.Zero);
                Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16), Is.EqualTo(multipart ? 8 : 5));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void DenseFramingCopiesTheRemainderOnlyOnceAndRetainsThePartialTail()
        {
            var connection = new ProbeConnection();
            try
            {
                string response = Frame(0x80000015);
                string burst = string.Concat(Enumerable.Repeat(response, 2000)) + response.Substring(0, 20);
                // Warm up parsing and dispatch before measuring allocations on the parsing thread.
                connection.MessageHandler(response);
#if !NETFRAMEWORK
                long before = GC.GetAllocatedBytesForCurrentThread();
#endif
                connection.MessageHandler(burst);
#if !NETFRAMEWORK
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.That(allocated, Is.LessThan(8_000_000), "Repeatedly copying the unconsumed tail allocates over 100 MB for this burst");
#endif
                connection.MessageHandler(response.Substring(20));
                Assert.That(connection.Reconnects, Is.Zero, "The partial tail must retain its boundary");
            }
            finally { connection.Quit(); }
        }
    }
}
