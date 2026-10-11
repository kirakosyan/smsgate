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
    [TestFixture, NonParallelizable]
    public partial class ReviewFeedbackRegressionTests
    {
        private static string Frame(uint command, string body = "", uint sequence = 42, uint status = 0) =>
            $"{16 + body.Length / 2:X8}{command:X8}{status:X8}{sequence:X8}{body}";

        private static string DataFrame(string payload, string format = "ascii", string recipient = "2", bool udhi = true) =>
            Frame(0x103, "00000031000000" + Common.StringToHex(recipient) + "00" +
                (udhi ? "40" : "00") + "00" + Common.body_format[format].ToString("X2") +
                $"0424{payload.Length / 2:X4}{payload}");

        private static string[] IncomingParts(string command, string body, string recipient = "2")
        {
            if (command != "data") return Encode(command, body, recipient: recipient);
            return new[]
            {
                DataFrame("0500032A0201" + Common.StringToHex(body.Substring(0, 153)), recipient: recipient),
                DataFrame("0500032A0202" + Common.StringToHex(body.Substring(153)), recipient: recipient)
            };
        }

        [Test]
        public void AcknowledgedSegmentsSurviveEveryTransientClientDisconnect(
            [Values("submit", "deliver", "data")] string command,
            [Values("eof", "reset", "unbind", "timeout", "bad-frame")] string failure)
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            string body = new string('a', 180);
            var parts = IncomingParts(command, body);
            WritePdu(peer.Peer.GetStream(), parts[0]);
            Assert.That(ReadPdu(peer.Peer.GetStream()).Substring(16, 8), Is.EqualTo("00000000"));
            switch (failure)
            {
                case "unbind":
                    WritePdu(peer.Peer.GetStream(), Frame(6));
                    Assert.That(ReadPdu(peer.Peer.GetStream()).Substring(8, 8), Is.EqualTo("80000006"));
                    break;
                case "timeout":
                    peer.Client.timeout = 60;
                    peer.Client.keep_alive = 30;
                    ReadPdu(peer.Peer.GetStream());
                    WaitUntil(() => !peer.Client.connected, "The unanswered keepalive must end the old session");
                    peer.Client.timeout = 3000;
                    peer.Client.keep_alive = 60000;
                    break;
                case "bad-frame": WritePdu(peer.Peer.GetStream(), "0000000F"); break;
                default:
                    if (failure == "reset") peer.Peer.Client.LingerState = new LingerOption(true, 0);
                    peer.Peer.Dispose();
                    break;
            }
            peer.Accept();
            peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), parts[1]);
            ReadPdu(peer.Peer.GetStream());
            WaitUntil(() => messages.Count == 1, "An ACKed segment must survive the reconnect");
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { body }));
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void WriteFailurePreservesSegmentsButExpiryAndExplicitQuitDoNot(string command)
        {
            using var first = new SocketPair();
            using var second = new SocketPair();
            var messages = new List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Add(body);
            var connection = new ProbeConnection(events);
            string body = new string('w', 180);
            var parts = IncomingParts(command, body);
            try
            {
                connection.Attach(first.Local);
                connection.MessageHandler(parts[0]);
                ReadPdu(first.Remote.GetStream());
                first.Local.Dispose();
                Assert.That(connection.SendPDU(Frame(0x15)), Is.EqualTo(502));
                connection.Attach(second.Local);
                connection.MessageHandler(parts[1]);
                Assert.That(messages, Is.EqualTo(new[] { body }));
                connection.MessageHandler(parts[0]);
                connection.Drop();
                connection.AgeMultipart();
                connection.MessageHandler(parts[1]);
                Assert.That(messages, Has.Count.EqualTo(1), "Expired parts must not be joined after a reconnect");
                Assert.That(connection.MultipartCount, Is.EqualTo(1));
                connection.Quit();
                Assert.That(connection.MultipartCount, Is.Zero);
            }
            finally { connection.Quit(); }
        }

        [TestCase("submit")]
        [TestCase("deliver")]
        [TestCase("data")]
        public void ServerRebindAlsoRetainsAcknowledgedSegments(string command)
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var host = new ServerHost(events);
            string body = new string('b', 180);
            var parts = IncomingParts(command, body);
            using (var first = host.Bind(9))
            {
                WritePdu(first.GetStream(), parts[0]);
                ReadPdu(first.GetStream());
                WritePdu(first.GetStream(), Frame(6));
                ReadPdu(first.GetStream());
                WaitUntil(() => !host.Server.connected, "Unbind must finish before the rebind");
            }
            using var second = host.Bind(9);
            WritePdu(second.GetStream(), parts[1]);
            ReadPdu(second.GetStream());
            WaitUntil(() => messages.Count == 1, "The reassembled message should be delivered once");
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { body }));
        }

        [TestCase(SocketError.ConnectionAborted)]
        [TestCase(SocketError.ConnectionReset)]
        [TestCase(SocketError.TooManyOpenSockets)]
        [TestCase(SocketError.NoBufferSpaceAvailable)]
        public async Task TransientAcceptErrorsRetryAndStillAcceptTheNextClient(SocketError error)
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var client = new TcpClient();
            int calls = 0, failures = 0, accepted = 0;
            await Server.RunAcceptLoop(_ => ++calls <= 2
                    ? ValueTask.FromException<TcpClient>(new SocketException((int)error))
                    : ValueTask.FromResult(client),
                value => { Assert.That(value, Is.SameAs(client)); accepted++; cancellation.Cancel(); },
                _ => failures++, cancellation.Token);
            Assert.That(calls, Is.EqualTo(3));
            Assert.That(failures, Is.EqualTo(2));
            Assert.That(accepted, Is.EqualTo(1));
        }

        [Test]
        public async Task StopCancelsTheAcceptRetryBackoff()
        {
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            await Server.RunAcceptLoop(_ =>
                {
                    calls++;
                    return ValueTask.FromException<TcpClient>(new SocketException((int)SocketError.ConnectionAborted));
                }, _ => Assert.Fail("No connection should be accepted"), _ => cancellation.Cancel(), cancellation.Token);
            Assert.That(calls, Is.EqualTo(1));
        }

        [Test]
        public void SustainedBufferedTrafficIsReadWithoutRecursionAndPreservesOrder()
        {
            var messages = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => messages.Enqueue(body);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            // Keep many reads ready synchronously; responses need no outbound traffic.
            string burst = string.Concat(Enumerable.Repeat(Frame(0x80000015), 50000)) +
                Encode("deliver", "last frame")[0];
            WritePdu(peer.Peer.GetStream(), burst);
            Assert.That(ReadPdu(peer.Peer.GetStream()).Substring(8, 8), Is.EqualTo("80000005"));
            WaitUntil(() => messages.Count == 1, "The frame after the burst must be read", 10000);
            Assert.That(messages.ToArray(), Is.EqualTo(new[] { "last frame" }));
            Assert.That(peer.Client.connected, Is.True);
        }

        private static IEnumerable<int[]> ResponseOrders => new[]
        {
            new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 },
            new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
        };

        [Test]
        public void MultipartHasOneTerminalStatusRegardlessOfResponseOrder(
            [ValueSource(nameof(ResponseOrders))] int[] order, [Values(-1, 0, 1, 2)] int rejectedPart,
            [Values(0x80000004u, 0x80000005u, 0x80000103u)] uint command)
        {
            var statuses = new List<(Common.MessageStatus, string)>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, id) => statuses.Add((status, id));
            var connection = new ProbeConnection(events);
            try
            {
                connection.TrackParts(10, 3, 42, DateTime.UtcNow);
                foreach (int part in order)
                {
                    var response = Frame(command, Common.StringToHex("part" + part) + "00", (uint)(10 + part), part == rejectedPart ? 0x58u : 0);
                    connection.MessageHandler(response);
                    connection.MessageHandler(response); // Duplicate ACKs must not change completion.
                    if (rejectedPart < 0 && connection.Pending != 0)
                        Assert.That(statuses, Is.Empty);
                }
                Assert.That(statuses, Is.EqualTo(new[]
                {
                    (rejectedPart < 0 ? Common.MessageStatus.sent : Common.MessageStatus.rejected,
                        "part" + (rejectedPart < 0 ? 0 : rejectedPart))
                }));
                Assert.That(connection.Pending, Is.Zero);
            }
            finally { connection.Quit(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ResponsesBetweenMultipartWritesCannotMoveStatusBackwards(bool rejected)
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var peer = new ClientPeer(events: events);
            peer.BindSuccessfully();
            peer.Client.multypart_delay = 100;
            var sending = Task.Run(() => peer.Client.SubmitSm(1, "1", "2", new string('a', 307)));
            for (int i = 0; i < 3; i++)
            {
                string request = ReadPdu(peer.Peer.GetStream());
                uint sequence = Convert.ToUInt32(request.Substring(24, 8), 16);
                WritePdu(peer.Peer.GetStream(), Frame(0x80000004, sequence: sequence, status: rejected && i == 0 ? 0x58u : 0));
            }
            Assert.That(sending.Wait(TimeSpan.FromSeconds(4)), Is.True);
            var terminal = rejected ? Common.MessageStatus.rejected : Common.MessageStatus.sent;
            WaitUntil(() => statuses.Contains(terminal), "Submission must complete");
            peer.Client.DrainNotificationsAsync().GetAwaiter().GetResult();
            Assert.That(statuses.ToArray(), Is.EqualTo(new[] { Common.MessageStatus.submitted_waiting_for_ACK, terminal }));
        }

        [Test]
        public void RejectionIsNotReplacedByTimeoutWhenAnotherPartIsMissing()
        {
            using var pair = new SocketPair();
            var statuses = new List<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Add(status);
            var connection = new ProbeConnection(events) { timeout = 60000 };
            try
            {
                connection.Attach(pair.Local);
                connection.TrackParts(10, 3, 1, DateTime.UtcNow.AddMinutes(-10));
                connection.MessageHandler(Frame(0x80000004, sequence: 10, status: 0x58));
                connection.Tick();
                connection.Drop();
                Assert.That(statuses, Is.EqualTo(new[] { Common.MessageStatus.rejected }));
                Assert.That(connection.Pending, Is.Zero);
            }
            finally { connection.Quit(); }
        }
    }
}
