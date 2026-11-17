using System;
using System.Diagnostics.Metrics;
using System.IO;
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
    public class NotificationAndReportTests
    {
        [Test]
        public async Task QueueAndInFlightMetricsRecoverExactTotalsAfterNotificationOverload()
        {
            using var pair = new SocketPair();
            using var client = new ProbeConnection
            {
                MaxPendingNotifications = 1,
                SubmissionOptions = new() { MaxInFlightPdus = 1, QueueCapacity = 1 }
            };
            long queued = 0, inFlight = 0;
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) =>
            {
                if (instrument == SmppDiagnostics.Queue || instrument == SmppDiagnostics.InFlight)
                    current.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<long>((instrument, change, _, _) =>
            {
                if (instrument == SmppDiagnostics.Queue) Interlocked.Add(ref queued, change);
                else Interlocked.Add(ref inFlight, change);
            });
            listener.Start();
            using var entered = new ManualResetEventSlim();
            using var unblock = new ManualResetEventSlim();
            client.StateChanged += state =>
            {
                if (state.State != ConnectionState.Bound) return;
                entered.Set();
                unblock.Wait(TimeSpan.FromSeconds(5));
            };
            client.Attach(pair.Local);
            Assert.That(entered.Wait(TimeSpan.FromSeconds(3)), Is.True);
            Task<SubmissionResult> first, second;
            SubmitSm firstPdu;
            try
            {
                first = client.SubmitAsync(new(1, "1", "2", "first"));
                firstPdu = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
                second = client.SubmitAsync(new(2, "1", "2", "second"));
                Assert.That(client.DroppedNotifications, Is.GreaterThan(0));
            }
            finally { unblock.Set(); }
            client.Drain();
            Assert.That(Interlocked.Read(ref queued), Is.EqualTo(1));
            Assert.That(Interlocked.Read(ref inFlight), Is.EqualTo(1));
            client.MessageHandler(new SubmitSmResp { sequence_number = firstPdu.sequence_number, MessageID = "one" }.Encode());
            var secondPdu = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
            client.MessageHandler(new SubmitSmResp { sequence_number = secondPdu.sequence_number, MessageID = "two" }.Encode());
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
            client.Drain();
            Assert.That(Interlocked.Read(ref queued), Is.Zero);
            Assert.That(Interlocked.Read(ref inFlight), Is.Zero);
        }

        [Test]
        public async Task SlowSubscriberThrottlesBeforeAcknowledgementAndResumesInOrder()
        {
            using var entered = new ManualResetEventSlim();
            using var unblock = new ManualResetEventSlim();
            var bodies = new System.Collections.Generic.List<string>();
            var events = new GateEvents();
            events.NewMessageEvent += (_, _, _, _, body, _, _) =>
            {
                entered.Set();
                unblock.Wait(TimeSpan.FromSeconds(10));
                bodies.Add(body);
            };
            using var pair = new SocketPair();
            using var server = new ProbeConnection(events, server: true) { MaxPendingNotifications = 2 };
            server.Attach(pair.Local);
            uint Receive(string body)
            {
                ((Connection)server).MessageHandler(Encode("submit", body)[0]);
                return new SubmitSmResp(ReadPdu(pair.Remote.GetStream())).command_status;
            }
            try
            {
                Assert.That(Receive("one"), Is.Zero);
                Assert.That(entered.Wait(TimeSpan.FromSeconds(3)), Is.True);
                Assert.That(Receive("two"), Is.Zero);
                Assert.That(Receive("three"), Is.Zero);
                Assert.That(Receive("four"), Is.EqualTo(0x58));
                Assert.That(server.DroppedNotifications, Is.GreaterThan(0));
            }
            finally { unblock.Set(); }
            await server.DrainNotificationsAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(Receive("four"), Is.Zero);
            server.Drain();
            Assert.That(bodies, Is.EqualTo(new[] { "one", "two", "three", "four" }));
        }

        [Test]
        public void DisconnectedReportWriteExposesFailure()
        {
            using var server = new Server("offline-report", new GateEvents());
            Assert.That(server.TrySubmitDeliveryReport("id", Common.MessageStatus.rejected), Is.False);
            Assert.Throws<InvalidOperationException>(() => server.SubmitDeliveryReport("id", Common.MessageStatus.rejected));
        }

        [TestCase(0u)]
        [TestCase(8u)]
        public async Task ReportWaitsForMatchingResponseAndReturnsRawStatus(uint status)
        {
            using var pair = new SocketPair();
            using var server = new ProbeConnection(server: true);
            server.Attach(pair.Local);
            var result = server.SubmitDeliveryReportAsync("42", Common.MessageStatus.delivered_ACK_received);
            var request = new DeliverSm(ReadPdu(pair.Remote.GetStream()));
            server.MessageHandler(new SubmitSmResp { sequence_number = request.sequence_number }.Encode());
            Assert.That(result.IsCompleted, Is.False);
            server.MessageHandler(new DeliverSmResp { sequence_number = request.sequence_number, command_status = status }.Encode());
            Assert.That(await result.WaitAsync(TimeSpan.FromSeconds(3)), Is.EqualTo(status));
        }

        [Test]
        public void ReportAwaitingResponseFailsOnDisconnect()
        {
            using var pair = new SocketPair();
            using var server = new ProbeConnection(server: true);
            server.Attach(pair.Local);
            var result = server.SubmitDeliveryReportAsync("42", Common.MessageStatus.delivered_ACK_received);
            ReadPdu(pair.Remote.GetStream());
            server.Drop();
            Assert.ThrowsAsync<IOException>(async () => await result);
        }

        [Test]
        public async Task AcceptedSubmissionExpiresWhileItsTransportIsOffline()
        {
            var expired = new TaskCompletionSource<MessageDeliveryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new GateEvents();
            events.MessageDeliveryStatusEvent += status => { if (status.IsComplete) expired.TrySetResult(status); };
            using var pair = new SocketPair();
            using var client = new ProbeConnection(events);
            client.DeliveryTrackingLifetime = TimeSpan.MaxValue;
            client.Attach(pair.Local);
            Assert.That(client.SubmitSm(7, "1", "2", "hello"), Is.Zero);
            var sent = new SubmitSm(ReadPdu(pair.Remote.GetStream()));
            client.MessageHandler(new SubmitSmResp { sequence_number = sent.sequence_number, MessageID = "provider-id" }.Encode());
            client.Drop();
            Assert.That(expired.Task.IsCompleted, Is.False);
            client.DeliveryTrackingLifetime = TimeSpan.FromMilliseconds(100);
            var status = await expired.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(status.MessageId, Is.EqualTo(7));
            Assert.That(status.Status, Is.EqualTo(Common.MessageStatus.timeout));
        }
    }
}
