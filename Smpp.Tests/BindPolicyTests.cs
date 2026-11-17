using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Responses;
using static Smpp.Tests.ReviewTestSupport;
using ClientPeer = Smpp.Tests.SessionRegressionTests.ClientPeer;
using ServerHost = Smpp.Tests.ServerSessionRegressionTests.ServerHost;

namespace Smpp.Tests
{
    [TestFixture, NonParallelizable]
    public class BindPolicyTests
    {
        [TestCase(1, "submit", 4)]
        [TestCase(2, "submit", 0)]
        [TestCase(9, "deliver", 4)]
        [TestCase(1, "data", 0)]
        [TestCase(2, "data", 0)]
        public void ServerChecksTheNegotiatedModeBeforeAcceptingAMessage(int mode, string command, int expectedStatus)
        {
            var received = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.Enqueue(body);
            using var host = new ServerHost(events);
            using var peer = host.Bind(mode);
            WritePdu(peer.GetStream(), Encode(command, "hello")[0]);
            string response = ReadPdu(peer.GetStream());
            uint request = command switch { "submit" => 4u, "deliver" => 5u, _ => 0x103u };
            Assert.That(Convert.ToUInt32(response.Substring(8, 8), 16), Is.EqualTo(request | 0x80000000));
            Assert.That(Convert.ToUInt32(response.Substring(16, 8), 16), Is.EqualTo(expectedStatus));
            // The ACK write can finish before the receive loop enqueues the application event.
            // A subsequent exchange waits for the first request to finish dispatching in both cases.
            WritePdu(peer.GetStream(), "00000010000000150000000000000077");
            Assert.That(ReadPdu(peer.GetStream()), Is.EqualTo("00000010800000150000000000000077"));
            host.Server.DrainNotificationsAsync().GetAwaiter().GetResult();
            Assert.That(received.Count, Is.EqualTo(expectedStatus == 0 ? 1 : 0));
        }

        [Test]
        public void ServerCredentialCanRestrictBindDirections()
        {
            using var host = new ServerHost();
            host.Server.direction_in = false;
            using var refused = host.Bind(9, expectedStatus: 13);
            using var peer = host.Bind(1);
            WritePdu(peer.GetStream(), Encode("data", "hello")[0]);
            Assert.That(ReadPdu(peer.GetStream()).Substring(16, 8), Is.EqualTo("00000004"));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ClientRequiresABoundReceiveSessionForDeliverSm(bool transmitter)
        {
            int callbacks = 0;
            using var peer = new ClientPeer(mode: transmitter ? 2 : 9);
            peer.Client.InboundMessageHandler = (_, _) =>
            {
                System.Threading.Interlocked.Increment(ref callbacks);
                return Task.FromResult(MessageAcknowledgement.Accept());
            };
            if (transmitter) peer.BindSuccessfully();
            WritePdu(peer.Peer.GetStream(), Encode("deliver", "hello")[0]);
            Assert.That(ReadPdu(peer.Peer.GetStream()), Is.EqualTo(
                new DeliverSmResp { sequence_number = 42, command_status = 4 }.Encode()));
            Assert.That(callbacks, Is.Zero);
        }

        [Test]
        public async Task ReceiverBindRejectsAllOutboundSubmitApisBeforeWriting()
        {
            using var peer = new ClientPeer(mode: 1);
            peer.BindSuccessfully();
            peer.Client.direction_out = true; // A new configuration takes effect at the next bind.
            Assert.That(peer.Client.SubmitSm(1, "1", "2", "hello"), Is.EqualTo(1));
            var result = await peer.Client.SubmitAsync(new OutboundMessage(1, "1", "2", "hello"));
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Failed));
            Assert.That(result.MayHaveBeenAccepted, Is.False);
            Assert.That(peer.Client.SendPDU(Encode("submit", "hello")[0]), Is.EqualTo(1));
            Assert.That(peer.Peer.GetStream().DataAvailable, Is.False);
        }

        [Test]
        public async Task ResponseMatchesBothWrittenPartAndRequestCommand()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            connection.Attach(pair.Local);
            var sending = connection.SubmitAsync(new OutboundMessage(1, "1", "2", new string('a', 161)));
            uint first = Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16);
            connection.MessageHandler(new DataSmResp { sequence_number = first, MessageID = "other" }.Encode());
            connection.MessageHandler(new GenericNack { sequence_number = first + 1, command_status = 3 }.Encode());
            connection.MessageHandler(new SubmitSmResp { sequence_number = first + 1, MessageID = "early" }.Encode());
            Assert.That(connection.Pending, Is.EqualTo(2));
            connection.MessageHandler(new SubmitSmResp { sequence_number = first, MessageID = "one" }.Encode());
            Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(24, 8), 16), Is.EqualTo(first + 1));
            connection.MessageHandler(new SubmitSmResp { sequence_number = first + 1, MessageID = "two" }.Encode());
            var result = await sending.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Parts.Select(part => part.ProviderMessageId), Is.EqualTo(new[] { "one", "two" }));
        }

        [Test]
        public void KeepaliveResponseCannotClearTheBindDeadline()
        {
            using var peer = new ClientPeer();
            peer.Client.timeout = 150;
            peer.Respond(command: 0x80000015);
            peer.Accept();
            peer.BindSuccessfully();
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(9)]
        [TestCase(11)]
        public void DebugNotificationsOmitInboundCredentialBodies(int request)
        {
            var logs = new ConcurrentQueue<(string Name, string Payload)>();
            var events = new GateEvents();
            events.ChannelEvent += (_, name, payload) => logs.Enqueue((name, payload));
            using var peer = new ClientPeer(events: events);
            peer.Client.debug = true;
            peer.BindSuccessfully();
            string frame = peer.Bind.Substring(0, 8) + request.ToString("X8") + peer.Bind.Substring(16);
            peer.Client.MessageHandler(frame);
            peer.Client.DrainNotificationsAsync().GetAwaiter().GetResult();
            Assert.That(logs.Any(entry => entry.Name == $"Receiving [{Common.command_id[(uint)request]}]" &&
                entry.Payload == ""), Is.True);
            Assert.That(logs.Any(entry => entry.Payload?.Contains(Common.StringToHex("password")) == true), Is.False);
        }

        [Test]
        public void ListenerBoundsPendingHandshakesAndReleasesTheSlotOnBind()
        {
            using var host = new ServerHost(maxPendingHandshakes: 1);
            using var pending = new TcpClient("127.0.0.1", host.Port);
            WaitUntil(() => Server.PendingHandshakeCount == 1, "The first socket should occupy the handshake slot");
            using (var excess = new TcpClient("127.0.0.1", host.Port))
            {
                excess.ReceiveTimeout = 3000;
                Assert.That(excess.GetStream().ReadByte(), Is.EqualTo(-1));
            }
            var bind = new Smpp.Requests.BindTransceiver { SystemID = host.Login, Password = "password", sequence_number = 1 };
            WritePdu(pending.GetStream(), bind.Encode());
            Assert.That(ReadPdu(pending.GetStream()).Substring(16, 8), Is.EqualTo("00000000"));
            WaitUntil(() => host.Server.connected && Server.PendingHandshakeCount == 0,
                "The bound session should have released its handshake slot");
            // A subsequent socket reaches authentication while the bound session remains connected.
            using var next = host.Bind(1, expectedStatus: 5);
        }
    }
}
