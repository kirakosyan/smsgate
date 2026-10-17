using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class ServiceIntegrationTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedStoreCorrelatesReceiverReceiptsWithTransmitterSubmissions(bool early)
        {
            var store = new DeliveryTrackingStore();
            var results = new ConcurrentQueue<MessageDeliveryStatus>();
            var txEvents = new GateEvents();
            txEvents.MessageDeliveryStatusEvent += results.Enqueue;
            using var tx = new ProbeConnection(txEvents) { DeliveryTrackingStore = store };
            using var rx = new ProbeConnection { DeliveryTrackingStore = store };
            using var pair = new SocketPair();
            tx.Attach(pair.Local);
            var submission = tx.SubmitAsync(new(42, "1", "2", "hello"));
            var request = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
            if (early) rx.MessageHandler(DeliveryTrackingTests.Receipt("split-id", 2));
            tx.MessageHandler(new SubmitSmResp { sequence_number = request.sequence_number, MessageID = "split-id" }.Encode());
            await submission.WaitAsync(TimeSpan.FromSeconds(3));
            if (!early) rx.MessageHandler(DeliveryTrackingTests.Receipt("split-id", 2));
            tx.Drain();
            Assert.That(results.Single().Status, Is.EqualTo(Common.MessageStatus.delivered_ACK_received));
            Assert.That(results.Single().MessageId, Is.EqualTo(42));
        }

        [Test]
        public async Task ClosingReceiverDoesNotExpireTransmitterTracking()
        {
            var store = new DeliveryTrackingStore();
            var results = new ConcurrentQueue<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessageDeliveryStatusEvent += results.Enqueue;
            using var tx = new ProbeConnection(events) { DeliveryTrackingStore = store };
            using var rx = new ProbeConnection { DeliveryTrackingStore = store };
            using var pair = new SocketPair();
            tx.Attach(pair.Local);
            var task = tx.SubmitAsync(new(42, "1", "2", "hello"));
            var request = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
            tx.MessageHandler(new SubmitSmResp { sequence_number = request.sequence_number, MessageID = "owned" }.Encode());
            await task.WaitAsync(TimeSpan.FromSeconds(3));
            rx.Quit();
            tx.Drain();
            Assert.That(results, Is.Empty);
            using var replacement = new ProbeConnection { DeliveryTrackingStore = store };
            replacement.MessageHandler(DeliveryTrackingTests.Receipt("owned", 2));
            tx.Drain();
            Assert.That(results.Single().IsComplete, Is.True);
        }

        [TestCase(0u)]
        [TestCase(13u)]
        public async Task ConnectAndBindWaitsForTheResponseAndExposesRawRejection(uint status)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new SmppClientOptions
            {
                Host = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                SystemId = "system",
                Password = "password"
            }.CreateClient(new GateEvents());
            var binding = client.ConnectAndBindAsync();
            using var peer = await listener.AcceptTcpClientAsync();
            var request = new BindTransceiver(ReadPdu(peer.GetStream()));
            Assert.That(binding.IsCompleted, Is.False);
            WritePdu(peer.GetStream(), new BindTransceiverResp { sequence_number = request.sequence_number, command_status = status }.Encode());
            var result = await binding.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Bound, Is.EqualTo(status == 0));
            Assert.That(result.CommandStatus, Is.EqualTo(status));
            if (status == 0) Assert.That(client.State, Is.EqualTo(ConnectionState.Bound));
        }

        [Test]
        public async Task CancellingBindWaitDoesNotCancelAnotherCallersSharedAttempt()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new SmppClientOptions
            {
                Host = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                SystemId = "system",
                Password = "password"
            }.CreateClient(new GateEvents());
            using var cancellation = new CancellationTokenSource();
            var cancelled = client.ConnectAndBindAsync(cancellation.Token);
            using var peer = await listener.AcceptTcpClientAsync();
            var request = new BindTransceiver(ReadPdu(peer.GetStream()));
            var other = client.ConnectAndBindAsync();
            cancellation.Cancel();
            Assert.That(async () => await cancelled, Throws.InstanceOf<OperationCanceledException>());
            WritePdu(peer.GetStream(), new BindTransceiverResp { sequence_number = request.sequence_number }.Encode());
            Assert.That((await other.WaitAsync(TimeSpan.FromSeconds(3))).Bound, Is.True);
        }

        [Test]
        public async Task InstanceOwnedClientReconnectsWithoutStaticRegistration()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            using var client = new SmppClientOptions
            {
                Host = "127.0.0.1",
                Port = ((IPEndPoint)listener.LocalEndpoint).Port,
                SystemId = "system",
                Password = "password",
                ReconnectDelayMilliseconds = 20
            }.CreateClient(new GateEvents());
            var binding = client.ConnectAndBindAsync();
            using (var first = await listener.AcceptTcpClientAsync())
            {
                var request = new BindTransceiver(ReadPdu(first.GetStream()));
                WritePdu(first.GetStream(), new BindTransceiverResp { sequence_number = request.sequence_number }.Encode());
                Assert.That((await binding.WaitAsync(TimeSpan.FromSeconds(3))).Bound, Is.True);
            }
            using var replacement = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(3));
            var next = new BindTransceiver(ReadPdu(replacement.GetStream()));
            WritePdu(replacement.GetStream(), new BindTransceiverResp { sequence_number = next.sequence_number }.Encode());
            WaitUntil(() => client.connected, "Owned client should reconnect independently of Gate.Clients");
        }

        [Test]
        public void OwnedGatewaysAreIsolatedAndCredentialsAreNotFormatted()
        {
            var options = new SmppClientOptions { Host = "localhost", SystemId = "system", Password = "secret" };
            using var first = new SmppGateway(new GateEvents());
            using var second = new SmppGateway(new GateEvents());
            var a = first.AddClient(options);
            var b = second.AddClient(options);
            Assert.That(a, Is.Not.SameAs(b));
            Assert.That(Gate.Clients.Values.Contains(a), Is.False);
            Assert.That(Gate.Clients.Values.Contains(b), Is.False);
            Assert.That(options.ToString(), Does.Not.Contain("secret"));
            Assert.Throws<ArgumentException>(() => first.AddClient(options));
            first.Dispose();
            Assert.That(a.State, Is.EqualTo(ConnectionState.Stopped));
            Assert.That(b.State, Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public void InvalidStartupOptionsFailBeforeRegisteringAClient()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SmppClientOptions { Host = "localhost", SystemId = "x", Password = "x", Port = 0 }.Validate());
            Assert.Throws<ArgumentException>(() => new SmppClientOptions { Host = "localhost", SystemId = "x", Password = "x", Receive = false, Transmit = false }.Validate());
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 0 } });
        }
    }
}
