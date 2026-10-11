using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture, NonParallelizable]
    public class SessionRegressionTests
    {
        [TestCase(0)]
        [TestCase(-1)]
        public void InvalidTimerIntervalsDoNotChangeTheExistingConfiguration(int interval)
        {
            var connection = new ProbeConnection();
            Assert.Throws<ArgumentOutOfRangeException>(() => connection.timeout = interval);
            Assert.Throws<ArgumentOutOfRangeException>(() => connection.keep_alive = interval);
            Assert.That(connection.timeout, Is.EqualTo(10000));
            Assert.That(connection.keep_alive, Is.EqualTo(60000));
            connection.Quit();
        }

        [Test]
        public void SendFailureIsReturnedAndPendingMessagesAreCompleted()
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection();
            try
            {
                connection.Attach(pair.Local);
                pair.Local.Dispose();
                Assert.That(connection.SubmitSm(1, "1", "2", "cannot send"), Is.EqualTo(502));
                Assert.That(connection.connected, Is.False);
                Assert.That(connection.Pending, Is.Zero);
                Assert.That(connection.Reconnects, Is.EqualTo(1));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void MultipartSequenceReservationWrapsWithoutZeroOrOutOfRangeValues()
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection { multypart_delay = 0 };
            try
            {
                connection.Attach(pair.Local);
                connection.SetNextSequence(0x7FFFFFFF);
                Assert.That(connection.SubmitSm(1, "1", "2", new string('a', 307)), Is.Zero);
                for (int i = 1; i <= 3; i++)
                    Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16), Is.EqualTo(i));
                Assert.That(connection.SubmitSm(2, "1", "2", "next"), Is.Zero);
                Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16), Is.EqualTo(4));
            }
            finally { connection.Quit(); }
        }

        private sealed class ClientPeer : IDisposable
        {
            internal readonly TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal readonly Client Client;
            internal TcpClient Peer;
            internal string Bind;
            internal ClientPeer(int mode = 9, GateEvents events = null)
            {
                Listener.Start();
                Client = new Client("review-" + Guid.NewGuid(), events ?? new GateEvents())
                {
                    host = "127.0.0.1", port = ((IPEndPoint)Listener.LocalEndpoint).Port,
                    system_id = "system", password = "password", reconnectDelay = 50, timeout = 3000,
                    direction_in = mode != 2, direction_out = mode != 1, multypart_delay = 1
                };
                Assert.That(Client.Connect(), Is.True);
                Accept();
            }
            internal void Accept()
            {
                Peer?.Dispose();
                Peer = Listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(4)).GetAwaiter().GetResult();
                Bind = ReadPdu(Peer.GetStream());
            }
            internal void Respond(uint status = 0, uint? sequence = null, uint? command = null)
            {
                uint request = Convert.ToUInt32(Bind.Substring(8, 8), 16);
                uint id = sequence ?? Convert.ToUInt32(Bind.Substring(24, 8), 16);
                WritePdu(Peer.GetStream(), $"00000010{command ?? (request | 0x80000000):X8}{status:X8}{id:X8}");
            }
            internal void BindSuccessfully()
            {
                Respond();
                WaitUntil(() => Client.connected, "Client should become bound after the matching success response");
            }
            public void Dispose() { Client.Quit(); Peer?.Dispose(); Listener.Stop(); }
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(9)]
        public async Task OnlyMatchingSuccessfulBindResponsesMakeTheClientConnected(int mode)
        {
            using var peer = new ClientPeer(mode);
            await Task.Delay(30); // Allow the old asynchronous write callback to run.
            Assert.That(peer.Client.connected, Is.False);
            Assert.That(peer.Client.SubmitSm(1, "1", "2", "early"), Is.EqualTo(1));
            peer.Respond(sequence: 999);
            peer.Respond(command: 0x80000000u | (mode == 9 ? 1u : 9u));
            await Task.Delay(30);
            Assert.That(peer.Client.connected, Is.False, "An unrelated response must not establish a session");
            peer.BindSuccessfully();
            Assert.That(peer.Client.Connect(), Is.True, "Connect is idempotent on an active session");
            Assert.That(peer.Listener.Pending(), Is.False);
        }

        [TestCase(1, 13u)]
        [TestCase(2, 13u)]
        [TestCase(9, 13u)]
        [TestCase(1, 0x123u)]
        [TestCase(2, 0x123u)]
        [TestCase(9, 0x123u)]
        public void RejectedBindsRemainDisconnectedAndRetryWithoutUnregistering(int mode, uint status)
        {
            using var peer = new ClientPeer(mode);
            peer.Respond(status);
            peer.Accept();
            Assert.That(peer.Client.connected, Is.False);
            peer.BindSuccessfully();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void EofAndSocketResetReconnectAndAllowTrafficOnTheNewSession(bool reset)
        {
            using var peer = new ClientPeer();
            peer.BindSuccessfully();
            if (reset) peer.Peer.Client.LingerState = new LingerOption(true, 0);
            peer.Peer.Dispose();
            peer.Accept();
            Assert.That(peer.Client.connected, Is.False);
            peer.BindSuccessfully();
            Assert.That(peer.Client.SubmitSm(1, "1", "2", "after reconnect"), Is.Zero);
            var message = ReadPdu(peer.Peer.GetStream());
            Assert.That(Decode("submit", message).Body, Is.EqualTo("after reconnect"));
        }

        [Test]
        public void RemoteUnbindIsAcknowledgedBeforeClientReconnects()
        {
            using var peer = new ClientPeer();
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), "0000001000000006000000000000002A");
            Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo("0000001080000006000000000000002A"));
            peer.Accept();
            peer.BindSuccessfully();
        }

        [Test]
        public async Task QuitCancelsPendingReconnectAndCannotBeUndoneByOldCallbacks()
        {
            using var peer = new ClientPeer();
            peer.Client.reconnectDelay = 150;
            peer.BindSuccessfully();
            peer.Peer.Dispose();
            WaitUntil(() => !peer.Client.connected, "EOF should clear connected");
            peer.Client.Quit();
            await Task.Delay(350);
            Assert.That(peer.Listener.Pending(), Is.False);
            Assert.That(peer.Client.connected, Is.False);
            Assert.That(peer.Client.Connect(), Is.False);
        }

        [Test]
        public void PartialFrameFromOldSessionCannotCorruptTheNewSession()
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), Encode("deliver", "stale")[0].Substring(0, 20));
            peer.Peer.Dispose();
            peer.Accept();
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), Encode("deliver", "fresh")[0]);
            WaitUntil(() => messages.Count == 1, "New session should process a complete frame");
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { "fresh" }));
        }

        [Test]
        public void IdleTimeoutTicksContinueAndEventuallyExpireAnUnacknowledgedSubmission()
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            peer.Client.timeout = 50;
            // Wait for several healthy ticks by using an explicit idle delay.
            Task.Delay(180).GetAwaiter().GetResult();
            Assert.That(peer.Client.SubmitSm(42, "1", "2", "needs ACK"), Is.Zero);
            ReadPdu(peer.Peer.GetStream());
            WaitUntil(() => statuses.Contains(Common.MessageStatus.timeout), "Missing submit_sm_resp must time out");
            peer.Accept();
            peer.BindSuccessfully();
        }

        [Test]
        public void BindTimeoutRetriesEvenWithoutAnyResponse()
        {
            using var peer = new ClientPeer();
            peer.Client.timeout = 50;
            peer.Accept();
            peer.BindSuccessfully();
        }

        [Test]
        public void ConcurrentMultipartSendsUseUniqueSequencesAndKeepAcknowledgementsCorrelated()
        {
            var statuses = new ConcurrentQueue<(int, Common.MessageStatus)>();
            var events = new GateEvents();
            events.MessageStatusEvent += (id, status, _) => statuses.Enqueue((id, status));
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            var senders = Enumerable.Range(1, 8).Select(id => Task.Run(() => peer.Client.SubmitSm(id, "1", "2", new string('a', 307)))).ToArray();
            var sequences = new System.Collections.Generic.HashSet<uint>();
            for (int i = 0; i < 24; i++)
            {
                string pdu = ReadPdu(peer.Peer.GetStream());
                uint sequence = Convert.ToUInt32(pdu.Substring(24, 8), 16);
                Assert.That(sequences.Add(sequence), Is.True, "Sequence must be reserved before another sender encodes");
                WritePdu(peer.Peer.GetStream(), $"000000108000000400000000{sequence:X8}");
            }
            Assert.That(Task.WhenAll(senders).Wait(TimeSpan.FromSeconds(4)), Is.True);
            Assert.That(senders.Select(task => task.Result), Is.All.Zero);
            WaitUntil(() => statuses.Count(value => value.Item2 == Common.MessageStatus.sent) == 24, "Every segment should be acknowledged");
            Assert.That(statuses.Any(value => value.Item2 == Common.MessageStatus.timeout), Is.False);
            foreach (int id in Enumerable.Range(1, 8))
                Assert.That(statuses.Where(value => value.Item1 == id).Last().Item2, Is.EqualTo(Common.MessageStatus.sent));
        }

        [Test]
        public void DebugBindLogsNeverContainCredentials()
        {
            var payloads = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.ChannelEvent += (_, _, pdu) => payloads.Enqueue(pdu);
            using var peer = new ClientPeer(events: events);
            peer.Client.debug = true;
            peer.Respond(13);
            peer.Accept();
            Assert.That(payloads.Any(pdu => pdu.Contains(Common.StringToHex("password"))), Is.False);
        }

        [Test]
        public void HealthyTimeoutChecksStayEnabledAndExpiredCommandsCleanPendingMessagesOnce()
        {
            using var pair = new SocketPair();
            var statuses = new ConcurrentQueue<int>();
            var events = new GateEvents();
            events.MessageStatusEvent += (id, status, _) => { if (status == Common.MessageStatus.timeout) statuses.Enqueue(id); };
            var connection = new ProbeConnection(events) { timeout = 60000 };
            try
            {
                connection.Attach(pair.Local);
                connection.Tick();
                connection.Tick();
                Assert.That(connection.TimerEnabled, Is.True);
                connection.Track(10, 42, DateTime.UtcNow);
                connection.Track(11, 42, DateTime.UtcNow);
                connection.Queue(12, DateTime.UtcNow.AddMinutes(-2));
                connection.Tick();
                connection.Tick();
                Assert.That(connection.connected, Is.False);
                Assert.That(connection.Pending, Is.Zero);
                Assert.That(statuses.ToArray(), Is.EqualTo(new[] { 42 }));
                Assert.That(connection.Reconnects, Is.EqualTo(1));
            }
            finally { connection.Quit(); }
        }

        [Test]
        public void AcknowledgedAndNotYetSentSegmentsDoNotTimeOut()
        {
            using var pair = new SocketPair();
            var connection = new ProbeConnection { timeout = 60000 };
            try
            {
                connection.Attach(pair.Local);
                connection.Track(10, 1, DateTime.UtcNow.AddMinutes(-2));
                connection.Track(11, 2, DateTime.MaxValue);
                connection.MessageHandler("0000001080000004000000000000000A");
                connection.Tick();
                Assert.That(connection.connected, Is.True);
                Assert.That(connection.TimerEnabled, Is.True);
                Assert.That(connection.Pending, Is.EqualTo(1));
            }
            finally { connection.Quit(); }
        }
    }
}
