using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
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
    public class ServiceReviewRegressionTests
    {
        private static uint Sequence(string pdu) => Convert.ToUInt32(pdu.Substring(24, 8), 16);
        private static void Ack(ProbeConnection connection, string pdu, uint status = 0) => connection.MessageHandler(
            new SubmitSmResp { sequence_number = Sequence(pdu), command_status = status, MessageID = status == 0 ? "provider-" + Sequence(pdu) : "" }.Encode());

        [TestCase(false)]
        [TestCase(true)]
        public async Task ThrottleRetriesPublishOneTerminalAggregateForTheFinalOutcome(bool exhausted)
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var deliveries = new ConcurrentQueue<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            events.MessageDeliveryStatusEvent += deliveries.Enqueue;
            using var connection = new ProbeConnection(events) { SubmissionOptions = new() { MaxThrottleRetries = 1, ThrottleRetryDelay = TimeSpan.FromMilliseconds(20) } };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            var sending = connection.SubmitAsync(new(42, "1", "2", "hello"));
            Ack(connection, ReadPdu(pair.Remote.GetStream()), 0x58);
            string retry = ReadPdu(pair.Remote.GetStream());
            connection.Drain();
            Assert.That(statuses, Does.Not.Contain(Common.MessageStatus.rejected));
            Assert.That(deliveries.Any(status => status.IsComplete), Is.False);
            Ack(connection, retry, exhausted ? 0x58u : 0);
            var result = await sending.WaitAsync(TimeSpan.FromSeconds(3));
            if (!exhausted) connection.MessageHandler(DeliveryTrackingTests.Receipt("provider-" + Sequence(retry), 2));
            connection.Drain();
            Assert.That(result.Attempts, Is.EqualTo(2));
            Assert.That(statuses.Count(status => status == Common.MessageStatus.rejected || status == Common.MessageStatus.sent), Is.EqualTo(1));
            Assert.That(statuses.Last(), Is.EqualTo(exhausted ? Common.MessageStatus.rejected : Common.MessageStatus.sent));
            Assert.That(deliveries.Single(status => status.IsComplete).Status,
                Is.EqualTo(exhausted ? Common.MessageStatus.rejected : Common.MessageStatus.delivered_ACK_received));
        }

        [TestCase(1)]
        [TestCase(400)]
        public async Task CancellationPublishesOneLegacyTimeoutEvenAfterALateAck(int bodyLength)
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var connection = new ProbeConnection(events) { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            using var pair = new SocketPair();
            using var cancellation = new CancellationTokenSource();
            connection.Attach(pair.Local);
            var sending = connection.SubmitAsync(new(42, "1", "2", new string('a', bodyLength)), cancellation.Token);
            string first = ReadPdu(pair.Remote.GetStream());
            cancellation.Cancel();
            Assert.That((await sending.WaitAsync(TimeSpan.FromSeconds(3))).Outcome, Is.EqualTo(SubmissionOutcome.Cancelled));
            Ack(connection, first);
            connection.Drain();
            Assert.That(statuses.First(), Is.EqualTo(Common.MessageStatus.submitted_waiting_for_ACK));
            Assert.That(statuses.Count(status => status == Common.MessageStatus.timeout), Is.EqualTo(1));
            Assert.That(statuses, Does.Not.Contain(Common.MessageStatus.sent));
        }

        [Test]
        public async Task QueuedSendsUseTheirOwnTraceParentsWithoutRetainingCallerAsyncLocals()
        {
            var ambient = new AsyncLocal<string>();
            var parents = new ConcurrentQueue<ActivitySpanId>();
            var scopes = new ConcurrentQueue<string>();
            using var listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SmppDiagnostics.Name,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
                ActivityStarted = activity => { parents.Enqueue(activity.ParentSpanId); scopes.Enqueue(ambient.Value); }
            };
            ActivitySource.AddActivityListener(listener);
            using var connection = new ProbeConnection();
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            ActivitySpanId firstParent;
            ActivitySpanId secondParent;
            using (var request = new Activity("request-one").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                firstParent = request.SpanId;
                ambient.Value = "first request scope";
                var send = connection.SubmitAsync(new(1, "1", "2", "first"));
                Ack(connection, ReadPdu(pair.Remote.GetStream()));
                await send.WaitAsync(TimeSpan.FromSeconds(3));
            }
            using (var request = new Activity("request-two").SetIdFormat(ActivityIdFormat.W3C).Start())
            {
                secondParent = request.SpanId;
                ambient.Value = "second request scope";
                var send = connection.SubmitAsync(new(2, "1", "2", "second"));
                Ack(connection, ReadPdu(pair.Remote.GetStream()));
                await send.WaitAsync(TimeSpan.FromSeconds(3));
            }
            ambient.Value = null;
            Assert.That(parents, Is.EqualTo(new[] { firstParent, secondParent }));
            Assert.That(scopes.All(string.IsNullOrEmpty), Is.True);
        }

        private sealed class FailingEncoderConnection(bool changePartCount, GateEvents events) : ProbeConnection(events)
        {
            protected override string[] EncodeSubmission(OutboundMessage message, uint sequence, byte reference)
            {
                if (message.MessageId == 2 && sequence != 1)
                {
                    if (changePartCount) return base.EncodeSubmission(message with { Body = new string('a', 400) }, sequence, reference);
                    throw new InvalidOperationException("Injected encoding failure before sending");
                }
                return base.EncodeSubmission(message, sequence, reference);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task PreSendFailuresExposeTheirCauseAndCannotHaveBeenAccepted(bool mismatch)
        {
            var logs = new ConcurrentQueue<string>();
            var events = new GateEvents();
            events.Event += (_, message) => logs.Enqueue(message);
            using var connection = new FailingEncoderConnection(mismatch, events) { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            var first = connection.SubmitAsync(new(1, "1", "2", "hold"));
            string pdu = ReadPdu(pair.Remote.GetStream());
            var next = connection.SubmitAsync(new(2, "1", "2", "queued"));
            Ack(connection, pdu);
            await first.WaitAsync(TimeSpan.FromSeconds(3));
            var failed = await next.WaitAsync(TimeSpan.FromSeconds(3));
            connection.Drain();
            Assert.That(failed.Outcome, Is.EqualTo(SubmissionOutcome.Failed));
            Assert.That(failed.MayHaveBeenAccepted, Is.False);
            Assert.That(failed.ErrorMessage, Is.Not.Empty);
            Assert.That(logs.Any(log => log.Contains(failed.ErrorMessage)), Is.True);
            Assert.That(pair.Remote.Available, Is.Zero);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task EarlyAcceptanceUsesTheSubmittersPolicyAndCannotRegressToEnroute(bool shared, bool acceptedIsFinal)
        {
            var deliveries = new ConcurrentQueue<MessageDeliveryStatus>();
            var events = new GateEvents();
            events.MessageDeliveryStatusEvent += deliveries.Enqueue;
            using var tx = new ProbeConnection(events) { AcceptedReceiptsAreFinal = acceptedIsFinal };
            using var rx = new ProbeConnection { AcceptedReceiptsAreFinal = !acceptedIsFinal };
            if (shared) tx.DeliveryTrackingStore = rx.DeliveryTrackingStore = new DeliveryTrackingStore();
            var receiver = shared ? rx : tx;
            using var pair = new SocketPair();
            tx.Attach(pair.Local);
            var sending = tx.SubmitAsync(new(42, "1", "2", "hello"));
            string pdu = ReadPdu(pair.Remote.GetStream());
            string id = "provider-" + Sequence(pdu);
            receiver.MessageHandler(DeliveryTrackingTests.Receipt(id, 6));
            receiver.MessageHandler(DeliveryTrackingTests.Receipt(id, 1));
            Ack(tx, pdu);
            await sending.WaitAsync(TimeSpan.FromSeconds(3));
            tx.Drain();
            Assert.That(deliveries.Last().IsComplete, Is.EqualTo(acceptedIsFinal));
            Assert.That(deliveries.Last().Parts.Single().Status, Is.EqualTo(Common.MessageStatus.received_routed));
            receiver.MessageHandler(DeliveryTrackingTests.Receipt(id, 2));
            tx.Drain();
            Assert.That(deliveries.Last().Status, Is.EqualTo(acceptedIsFinal ? Common.MessageStatus.received_routed : Common.MessageStatus.delivered_ACK_received));
        }

        [Test]
        public async Task ALocalFailureOnRetryStillPublishesATerminalLegacyStatus()
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var connection = new FailingEncoderConnection(false, events)
            { SubmissionOptions = new() { MaxThrottleRetries = 1, ThrottleRetryDelay = TimeSpan.FromMilliseconds(20) } };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            var sending = connection.SubmitAsync(new(2, "1", "2", "retry"));
            Ack(connection, ReadPdu(pair.Remote.GetStream()), 0x58);
            var result = await sending.WaitAsync(TimeSpan.FromSeconds(3));
            connection.Drain();
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Failed));
            Assert.That(result.Attempts, Is.EqualTo(2));
            Assert.That(result.MayHaveBeenAccepted, Is.False);
            Assert.That(statuses.Last(), Is.EqualTo(Common.MessageStatus.generic_error));
            Assert.That(statuses, Does.Not.Contain(Common.MessageStatus.rejected));
        }

        [Test]
        public async Task APartDeadlineExpiresTheWholeSubmissionAndReleasesTheWindow()
        {
            var statuses = new ConcurrentQueue<Common.MessageStatus>();
            var events = new GateEvents();
            events.MessageStatusEvent += (_, status, _) => statuses.Enqueue(status);
            using var connection = new ProbeConnection(events) { timeout = 150, SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            var sending = connection.SubmitAsync(new(1, "1", "2", new string('a', 400)));
            ReadPdu(pair.Remote.GetStream());
            var result = await sending.WaitAsync(TimeSpan.FromSeconds(3));
            connection.Drain();
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Unknown));
            Assert.That(result.Parts, Has.Count.EqualTo(3));
            Assert.That(result.Parts.Select(part => part.CommandStatus), Is.All.Null);
            Assert.That(connection.Pending, Is.Zero);
            Assert.That(statuses.Count(status => status == Common.MessageStatus.timeout), Is.EqualTo(1));
            var next = connection.SubmitAsync(new(2, "1", "2", "hello"));
            Ack(connection, ReadPdu(pair.Remote.GetStream()));
            Assert.That((await next.WaitAsync(TimeSpan.FromSeconds(3))).Accepted, Is.True);
        }

        [Test]
        public async Task SlowMetricListenersCannotBlockTheProtocolOrSharedStore()
        {
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var store = new DeliveryTrackingStore();
            using var connection = new ProbeConnection { DeliveryTrackingStore = store };
            using var receiver = new ProbeConnection { DeliveryTrackingStore = store };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            bool sessionLockHeld = true;
            bool storeLockHeld = true;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == SmppDiagnostics.Name && instrument.Name == "smpp.responses") current.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((_, _, _, _) =>
            {
                sessionLockHeld = connection.SessionLockHeld;
                storeLockHeld = Monitor.IsEntered(store.SyncRoot);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            });
            listener.Start();
            try
            {
                var sending = connection.SubmitAsync(new(42, "1", "2", "hello"));
                string pdu = ReadPdu(pair.Remote.GetStream());
                await Task.Run(() => ((Connection)connection).MessageHandler(new SubmitSmResp { sequence_number = Sequence(pdu), MessageID = "metric-id" }.Encode())).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(entered.Wait(2000), Is.True);
                Assert.That((await sending.WaitAsync(TimeSpan.FromSeconds(2))).Accepted, Is.True);
                await Task.Run(() => ((Connection)connection).MessageHandler("00000010000000150000000000000077")).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(ReadPdu(pair.Remote.GetStream()), Is.EqualTo("00000010800000150000000000000077"));
                await Task.Run(() => ((Connection)receiver).MessageHandler(DeliveryTrackingTests.Receipt("metric-id", 2))).WaitAsync(TimeSpan.FromSeconds(2));
                Assert.That(sessionLockHeld, Is.False);
                Assert.That(storeLockHeld, Is.False);
            }
            finally { release.Set(); connection.Drain(); }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void QuitClearsPrivateTrackingCachesAndRetainsSharedUnmatchedReceipts(bool shared)
        {
            using var connection = new ProbeConnection();
            if (shared) connection.DeliveryTrackingStore = new DeliveryTrackingStore();
            var store = connection.DeliveryTrackingStore;
            connection.MessageHandler(DeliveryTrackingTests.Receipt("early", 2));
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            for (int id = 1; id <= 2; id++)
            {
                connection.SubmitSm(id, "1", "2", "hello");
                string pdu = ReadPdu(pair.Remote.GetStream());
                connection.MessageHandler(new SubmitSmResp { sequence_number = Sequence(pdu), MessageID = "ambiguous" }.Encode());
            }
            Assert.That(store.EarlyReceipts.Count, Is.EqualTo(1));
            Assert.That(store.AmbiguousIds.Count, Is.EqualTo(1));
            connection.Quit();
            Assert.That(store.Parts.Count, Is.Zero);
            Assert.That(store.EarlyReceipts.Count, Is.EqualTo(shared ? 1 : 0));
            Assert.That(store.AmbiguousIds.Count, Is.EqualTo(shared ? 1 : 0));
        }

        [Test]
        public void ReplacingAConfiguredStoreFailsRatherThanDiscardingItsSettings()
        {
            var shared = new DeliveryTrackingStore();
            using var connection = new ProbeConnection { DeliveryTrackingLifetime = TimeSpan.FromHours(48) };
            Assert.Throws<InvalidOperationException>(() => connection.DeliveryTrackingStore = shared);
            Assert.That(connection.DeliveryTrackingLifetime, Is.EqualTo(TimeSpan.FromHours(48)));
            Assert.That(shared.Lifetime, Is.EqualTo(TimeSpan.FromHours(24)));
            using var other = new ProbeConnection { DeliveryTrackingStore = shared, MaxTrackedDeliveryParts = 20 };
            Assert.That(shared.Capacity, Is.EqualTo(20));
        }

        private sealed class FailingClient(bool simulateTimeout) : Client("failure-state", new GateEvents(), false)
        {
            protected override ValueTask ConnectTransportAsync(TcpClient transport, CancellationToken cancellation) => simulateTimeout
                ? new ValueTask(Task.Delay(Timeout.Infinite, cancellation))
                : new ValueTask(Task.FromException(new SocketException((int)System.Net.Sockets.SocketError.ConnectionRefused)));
            protected override void ScheduleReconnect(int delayMilliseconds) { }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedConnectsTransitionOutOfConnecting(bool timeout)
        {
            using var client = new FailingClient(timeout) { timeout = 50 };
            var changes = new ConcurrentQueue<ConnectionState>();
            client.StateChanged += change => changes.Enqueue(change.State);
            Assert.That(await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(3)), Is.False);
            await client.DrainNotificationsAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(client.State, Is.EqualTo(ConnectionState.Reconnecting));
            Assert.That(changes, Is.EqualTo(new[] { ConnectionState.Connecting, ConnectionState.Reconnecting }));
        }

        [Test]
        public void DisconnectedServersWaitForPeersInsteadOfReportingAReconnect()
        {
            using var connection = new ProbeConnection { is_server = true };
            using var pair = new SocketPair();
            connection.Attach(pair.Local);
            connection.Drop();
            Assert.That(connection.State, Is.EqualTo(ConnectionState.Disconnected));
        }

        [Test]
        public async Task TypedClientDefaultsPreserveLegacyInboundDecoding()
        {
            using var listener = new TestTcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) => received.TrySetResult(body);
            using var client = new SmppClientOptions
            {
                Host = "127.0.0.1",
                Port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port,
                SystemId = "system",
                Password = "password"
            }.CreateClient(events);
            var binding = client.ConnectAndBindAsync();
            using var peer = await listener.AcceptTcpClientAsync();
            var request = new BindTransceiver(ReadPdu(peer.GetStream()));
            WritePdu(peer.GetStream(), new BindTransceiverResp { sequence_number = request.sequence_number }.Encode());
            Assert.That((await binding.WaitAsync(TimeSpan.FromSeconds(3))).Bound, Is.True);
            WritePdu(peer.GetStream(), new DeliverSm(events) { Sender = "1", Recipient = "2", Body = "@_$", BodyFormat = "ascii", sequence_number = 99 }.EncodeM()[0]);
            Assert.That(new DeliverSmResp(ReadPdu(peer.GetStream())).command_status, Is.Zero);
            Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo("@_$"));
        }
    }
}
