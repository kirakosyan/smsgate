using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Responses;
using Smpp.Requests;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture]
    public class AsyncSubmissionTests
    {
        private static OutboundMessage Message(int id = 42, string body = "hello") => new(id, "1", "2", body);
        private static void Ack(ProbeConnection connection, string frame, uint status = 0) =>
            connection.MessageHandler(new SubmitSmResp { sequence_number = Convert.ToUInt32(frame.Substring(24, 8), 16), command_status = status, MessageID = status == 0 ? "id-" + Convert.ToUInt32(frame.Substring(24, 8), 16) : "" }.Encode());

        [Test]
        public async Task MultipartPdusRespectWindowAndExposeEveryAcknowledgement()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            connection.Attach(pair.Local);
            var submission = connection.SubmitAsync(Message(body: new string('a', 400)));
            for (int part = 0; part < 3; part++)
            {
                string frame = ReadPdu(pair.Remote.GetStream());
                await Task.Delay(40);
                Assert.That(pair.Remote.Available, Is.Zero, "No next PDU before a window slot is released.");
                Assert.That(submission.IsCompleted, Is.False);
                Ack(connection, frame);
            }
            var result = await submission.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Accepted, Is.True);
            Assert.That(result.Parts.Select(part => part.PartNumber), Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(result.Parts.Select(part => part.ProviderMessageId).Distinct().Count(), Is.EqualTo(3));
        }

        [Test]
        public async Task QueueSaturationAndQueuedCancellationAreBounded()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 1, QueueCapacity = 1 } };
            connection.Attach(pair.Local);
            var first = connection.SubmitAsync(Message(1));
            string frame = ReadPdu(pair.Remote.GetStream());
            using var cancellation = new CancellationTokenSource();
            var queued = connection.SubmitAsync(Message(2), cancellation.Token);
            Assert.That((await connection.SubmitAsync(Message(3))).Outcome, Is.EqualTo(SubmissionOutcome.QueueFull));
            cancellation.Cancel();
            Assert.That((await queued.WaitAsync(TimeSpan.FromSeconds(1))).Outcome, Is.EqualTo(SubmissionOutcome.Cancelled));
            Ack(connection, frame);
            Assert.That((await first.WaitAsync(TimeSpan.FromSeconds(3))).Accepted, Is.True);
            await connection.StopAsync().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That((await connection.SubmitAsync(Message(4))).Outcome, Is.EqualTo(SubmissionOutcome.Stopped));
        }

        [Test]
        public async Task CancellationAfterWritingRetainsUnknownOutcomeAndDoesNotSendUnsentParts()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            connection.Attach(pair.Local);
            using var cancellation = new CancellationTokenSource();
            var task = connection.SubmitAsync(Message(body: new string('a', 400)), cancellation.Token);
            string first = ReadPdu(pair.Remote.GetStream());
            cancellation.Cancel();
            var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Cancelled));
            Assert.That(result.MayHaveBeenAccepted, Is.True);
            Assert.That(pair.Remote.Available, Is.Zero);
            Ack(connection, first); // A late response still releases its slot and updates ordinary tracking.
            var next = connection.SubmitAsync(Message(43));
            Ack(connection, ReadPdu(pair.Remote.GetStream()));
            Assert.That((await next.WaitAsync(TimeSpan.FromSeconds(3))).Accepted, Is.True);
        }

        [TestCase(0x58, 2)]
        [TestCase(0x45, 1)]
        public async Task OnlyExplicitThrottleRejectionsAreRetried(int status, int attempts)
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxThrottleRetries = 1, ThrottleRetryDelay = TimeSpan.FromMilliseconds(20) } };
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message());
            Ack(connection, ReadPdu(pair.Remote.GetStream()), (uint)status);
            if (attempts == 2) Ack(connection, ReadPdu(pair.Remote.GetStream()));
            var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Attempts, Is.EqualTo(attempts));
            Assert.That(result.Accepted, Is.EqualTo(attempts == 2));
        }

        [Test]
        public async Task PartialAcceptanceIsNeverRetriedAsAWholeMessage()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxThrottleRetries = 2 } };
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message(body: new string('a', 161)));
            string first = ReadPdu(pair.Remote.GetStream());
            string second = ReadPdu(pair.Remote.GetStream());
            Ack(connection, first); Ack(connection, second, 0x58);
            var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Rejected));
            Assert.That(result.MayHaveBeenAccepted, Is.True);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(pair.Remote.Available, Is.Zero);
        }

        [Test]
        public async Task MissingResponseIsUnknownAndIsNeverRetried()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { timeout = 150, SubmissionOptions = new() { MaxThrottleRetries = 2 } };
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message());
            ReadPdu(pair.Remote.GetStream());
            var result = await task.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(result.Outcome, Is.EqualTo(SubmissionOutcome.Unknown));
            Assert.That(result.MayHaveBeenAccepted, Is.True);
            Assert.That(result.Attempts, Is.EqualTo(1));
            Assert.That(result.Parts.Single().CommandStatus, Is.Null);
            Assert.That(pair.Remote.Available, Is.Zero);
        }

        [Test]
        public async Task RateLimitCountsPdusInsideMultipartMessages()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { PdusPerSecond = 10 } };
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message(body: new string('a', 161)));
            string first = ReadPdu(pair.Remote.GetStream());
            var elapsed = Stopwatch.StartNew();
            string second = ReadPdu(pair.Remote.GetStream());
            Assert.That(elapsed.ElapsedMilliseconds, Is.GreaterThanOrEqualTo(60));
            Ack(connection, first); Ack(connection, second);
            Assert.That((await task.WaitAsync(TimeSpan.FromSeconds(3))).Accepted, Is.True);
        }

        [Test]
        public async Task GracefulStopWaitsForAcknowledgementsAndRejectsNewWork()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection();
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message());
            string frame = ReadPdu(pair.Remote.GetStream());
            var stop = connection.StopAsync();
            Assert.That(stop.IsCompleted, Is.False);
            Assert.That((await connection.SubmitAsync(Message(2))).Outcome, Is.EqualTo(SubmissionOutcome.Stopped));
            Ack(connection, frame);
            await stop.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That((await task).Accepted, Is.True);
            Assert.That(connection.State, Is.EqualTo(ConnectionState.Stopped));
            Assert.That(Convert.ToUInt32(ReadPdu(pair.Remote.GetStream()).Substring(8, 8), 16), Is.EqualTo((uint)Common.CommandId.unbind));
        }

        [Test]
        public async Task ForcedStopResolvesActiveAndQueuedCallers()
        {
            using var pair = new SocketPair();
            using var connection = new ProbeConnection { SubmissionOptions = new() { MaxInFlightPdus = 1 } };
            connection.Attach(pair.Local);
            var active = connection.SubmitAsync(Message(1));
            ReadPdu(pair.Remote.GetStream());
            var queued = connection.SubmitAsync(Message(2));
            using var cancellation = new CancellationTokenSource(50);
            Assert.That(async () => await connection.StopAsync(cancellation.Token), Throws.InstanceOf<OperationCanceledException>());
            Assert.That((await active.WaitAsync(TimeSpan.FromSeconds(3))).MayHaveBeenAccepted, Is.True);
            Assert.That((await queued.WaitAsync(TimeSpan.FromSeconds(3))).Outcome, Is.EqualTo(SubmissionOutcome.Stopped));
        }

        [Test]
        public async Task MetricsAndStructuredDiagnosticsExposeOutcomesWithoutBodies()
        {
            var measurements = new ConcurrentQueue<string>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, current) => { if (instrument.Meter.Name == SmppDiagnostics.Name) current.EnableMeasurementEvents(instrument); };
            listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => measurements.Enqueue(instrument.Name));
            listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => measurements.Enqueue(instrument.Name));
            listener.Start();
            using var pair = new SocketPair();
            using var connection = new ProbeConnection();
            var diagnostics = new ConcurrentQueue<ProtocolDiagnostic>();
            connection.Diagnostic += diagnostics.Enqueue;
            connection.Attach(pair.Local);
            var task = connection.SubmitAsync(Message(body: "private text"));
            Ack(connection, ReadPdu(pair.Remote.GetStream()));
            await task.WaitAsync(TimeSpan.FromSeconds(3));
            connection.Drain();
            Assert.That(measurements, Does.Contain("smpp.queue.depth"));
            Assert.That(measurements, Does.Contain("smpp.pdus.in_flight"));
            Assert.That(measurements, Does.Contain("smpp.ack.duration"));
            Assert.That(diagnostics.Any(item => item.Name == "pdu.response" && item.CommandStatus == 0), Is.True);
            Assert.That(string.Join(" ", diagnostics), Does.Not.Contain("private text"));
        }
    }
}
