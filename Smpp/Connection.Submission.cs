using System;
using System.Collections.Generic;
using System.Diagnostics;
using Smpp.Events;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Smpp
{
    public abstract partial class Connection
    {
        private TaskCompletionSource<BindResult> bindCompletion;
        protected Task<BindResult> CurrentBindTask => bindCompletion?.Task ?? Task.FromResult(new BindResult(false, null));
        private SubmissionOptions submissionOptions = new();
        private Channel<SendWork> sendQueue;
        private Task sendWorker;
        private readonly CancellationTokenSource submissionShutdown = new();
        private SemaphoreSlim pduSlots;
        private readonly SemaphoreSlim rateLock = new(1, 1);
        private long nextPduAt;
        private bool draining;
        private readonly Dictionary<uint, AsyncPart> asyncParts = new();
        private sealed class AsyncPart
        {
            internal readonly TaskCompletionSource<MessagePartStatus> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal bool HasSlot;
            internal bool Sent;
        }
        private sealed class SendWork
        {
            internal OutboundMessage Message;
            internal CancellationToken Cancellation;
            internal bool Started;
            internal int PartCount;
            internal ActivityContext ParentContext;
            internal Submission CurrentSubmission;
            internal bool AttemptSent;
            internal int AttemptNumber;
            internal readonly TaskCompletionSource<SubmissionResult> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        /// <summary>Limits for SubmitAsync. Set before the first asynchronous submission. Legacy SubmitSm remains a direct send.</summary>
        public SubmissionOptions SubmissionOptions
        {
            get { lock (SessionLock) return submissionOptions; }
            set
            {
                Guard.ThrowIfNull(value);
                value.Validate();
                lock (SessionLock)
                {
                    if (sendQueue != null) throw new InvalidOperationException("Submission limits cannot change after the queue starts.");
                    submissionOptions = value;
                }
            }
        }
        private SubmissionResult EmptyResult(OutboundMessage message, SubmissionOutcome outcome) => new(message.MessageId, outcome, Array.Empty<MessagePartStatus>());

        /// <summary>Queues a bounded submission and awaits every PDU response. Queue saturation returns QueueFull immediately.</summary>
        public async Task<SubmissionResult> SubmitAsync(OutboundMessage message, CancellationToken cancellationToken = default)
        {
            Guard.ThrowIfNull(message);
            Guard.ThrowIfNull(message.Body);
            Guard.ThrowIfNull(message.Sender);
            Guard.ThrowIfNull(message.Recipient);
            // Validate encoding before admitting work so malformed input is never silently queued.
            int partCount = EncodeSubmission(message, 1, 1).Length;
            var work = new SendWork { Message = message, Cancellation = cancellationToken, PartCount = partCount, ParentContext = Activity.Current?.Context ?? default };
            lock (SessionLock)
            {
                if (stopped || draining) return EmptyResult(message, SubmissionOutcome.Stopped);
                if (cancellationToken.IsCancellationRequested) return EmptyResult(message, SubmissionOutcome.Cancelled);
                if (!connected) return EmptyResult(message, SubmissionOutcome.NotConnected);
                if (sendQueue == null)
                {
                    submissionOptions.Validate();
                    pduSlots = new(submissionOptions.MaxInFlightPdus, submissionOptions.MaxInFlightPdus);
                    sendQueue = Channel.CreateBounded<SendWork>(new BoundedChannelOptions(submissionOptions.QueueCapacity)
                    { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
                    sendWorker = RunDetached(RunSendQueueAsync);
                }
                Measure(SmppDiagnostics.Queue, 1);
                if (!sendQueue.Writer.TryWrite(work))
                {
                    Measure(SmppDiagnostics.Queue, -1);
                    return EmptyResult(message, SubmissionOutcome.QueueFull);
                }
            }
            using var registration = cancellationToken.Register(() =>
            {
                lock (work)
                    if (!work.Started) work.Completion.TrySetResult(EmptyResult(message, SubmissionOutcome.Cancelled));
            });
            return await work.Completion.Task.ConfigureAwait(false);
        }

        private async Task RunSendQueueAsync()
        {
            using var jobs = new SemaphoreSlim(submissionOptions.MaxInFlightPdus);
            var running = new HashSet<Task>();
            try
            {
                while (await sendQueue.Reader.WaitToReadAsync(submissionShutdown.Token).ConfigureAwait(false))
                {
                    await jobs.WaitAsync(submissionShutdown.Token).ConfigureAwait(false);
                    if (!sendQueue.Reader.TryRead(out var work)) { jobs.Release(); continue; }
                    Measure(SmppDiagnostics.Queue, -1);
                    running.RemoveWhere(task => task.IsCompleted);
                    running.Add(RunWorkAsync(work, jobs));
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                while (sendQueue.Reader.TryRead(out var work))
                {
                    Measure(SmppDiagnostics.Queue, -1);
                    work.Completion.TrySetResult(EmptyResult(work.Message, SubmissionOutcome.Stopped));
                }
                await Task.WhenAll(running).ConfigureAwait(false);
            }
        }

        private async Task RunWorkAsync(SendWork work, SemaphoreSlim jobs)
        {
            SubmissionResult result = null;
            try
            {
                lock (work)
                {
                    if (work.Completion.Task.IsCompleted) return;
                    work.Started = true;
                }
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(work.Cancellation, submissionShutdown.Token);
                using var activity = SmppDiagnostics.ActivitySource.StartActivity("smpp.submit", ActivityKind.Producer, work.ParentContext);
                activity?.SetTag("smpp.channel", channel_name);
                for (int attempt = 1; attempt <= submissionOptions.MaxThrottleRetries + 1; attempt++)
                {
                    lock (SessionLock)
                    {
                        work.AttemptSent = false;
                        work.AttemptNumber = attempt;
                    }
                    result = (await SendAttemptAsync(work, cancellation.Token).ConfigureAwait(false)) with { Attempts = attempt };
                    // Retry only when every PDU was explicitly rejected as throttled. Partial/unknown outcomes are never retried.
                    if (attempt > submissionOptions.MaxThrottleRetries || result.Parts.Count == 0 ||
                        result.Parts.Any(part => part.CommandStatus != 0x58) || result.Outcome != SubmissionOutcome.Rejected) break;
                    Diagnose("submission.throttled");
                    await Task.Delay(TimeSpan.FromTicks(checked(submissionOptions.ThrottleRetryDelay.Ticks * attempt)), cancellation.Token).ConfigureAwait(false);
                }
                activity?.SetTag("smpp.outcome", result.Outcome.ToString());

            }
            catch (OperationCanceledException)
            {
                result = (result ?? EmptyResult(work.Message, SubmissionOutcome.Cancelled)) with
                { Outcome = stopped ? SubmissionOutcome.Stopped : SubmissionOutcome.Cancelled };
            }
            catch (Exception ex)
            {
                Diagnose("submission.failed");
                Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Submission failed: " + ex.Message));
                result = EmptyResult(work.Message, work.AttemptSent ? SubmissionOutcome.Unknown : SubmissionOutcome.Failed) with
                { MayHaveBeenAccepted = work.AttemptSent, ErrorMessage = ex.Message, Attempts = work.AttemptNumber };
            }
            finally
            {
                if (result != null)
                {
                    FinishWorkAggregates(work, result);
                    work.Completion.TrySetResult(result);
                }
                jobs.Release();
            }
        }

        private async Task<SubmissionResult> SendAttemptAsync(SendWork work, CancellationToken token)
        {
            var request = work.Message;
            uint first = 0;
            int generation;
            string[] frames;
            AsyncPart[] responses;
            lock (SessionLock)
            {
                if (stopped || !connected) return EmptyResult(request, stopped ? SubmissionOutcome.Stopped : SubmissionOutcome.NotConnected);
                token.ThrowIfCancellationRequested();
                if (!AllocateSubmission(request.MessageId, request.Sender, request.Recipient, request.RequestDeliveryReport, SubmissionCommand,
                    (sequence, reference) => EncodeSubmission(request, sequence, reference), work.PartCount, true,
                    out frames, out first, out var submission))
                    throw new InvalidOperationException("Unable to reserve SMPP sequences.");
                // Abandon a wholly throttled attempt only after its replacement has been allocated.
                if (work.CurrentSubmission != null) work.CurrentSubmission.Completed = work.CurrentSubmission.DeliveryCompleted = true;
                work.CurrentSubmission = submission;
                responses = new AsyncPart[frames.Length];
                for (int i = 0; i < frames.Length; i++) asyncParts.Add(first + (uint)i, responses[i] = new AsyncPart());
                generation = sessionGeneration;
            }
            bool cancelled = false;
            try
            {
                for (int i = 0; i < frames.Length; i++)
                {
                    await pduSlots.WaitAsync(token).ConfigureAwait(false);
                    try
                    {
                        await WaitForRateAsync(token).ConfigureAwait(false);
                        lock (SessionLock)
                        {
                            token.ThrowIfCancellationRequested();
                            int writeResult = SendTrackedPart(first + (uint)i, frames[i], generation, SubmissionCommand, responses[i], work);
                            if (writeResult != 0) break;
                        }
                    }
                    finally { if (!responses[i].HasSlot) pduSlots.Release(); }
                }
                RemoveUnsentParts();
                await Task.WhenAll(responses.Select(part => part.Completion.Task)).WaitAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { cancelled = true; }
            finally
            {
                RemoveUnsentParts();
            }
            var parts = responses.Where(part => part.Completion.Task.Status == TaskStatus.RanToCompletion).Select(part => part.Completion.Task.Result).ToArray();
            bool sent = responses.Any(part => part.Sent);
            var outcome = cancelled ? (stopped ? SubmissionOutcome.Stopped : SubmissionOutcome.Cancelled) :
                parts.Length != frames.Length || parts.Any(part => part.CommandStatus == null) ? SubmissionOutcome.Unknown :
                parts.All(part => part.CommandStatus == 0) ? SubmissionOutcome.Accepted : SubmissionOutcome.Rejected;
            return new SubmissionResult(request.MessageId, outcome, Array.AsReadOnly(parts))
            { MayHaveBeenAccepted = parts.Any(part => part.CommandStatus == 0) || (sent && (cancelled || outcome == SubmissionOutcome.Unknown)) };

            void RemoveUnsentParts()
            {
                lock (SessionLock) ExpireSubmission(work.CurrentSubmission, unsentOnly: true);
            }
        }

        private void FinishWorkAggregates(SendWork work, SubmissionResult result)
        {
            lock (SessionLock)
            {
                var submission = work.CurrentSubmission;
                if (submission == null) return;
                submission.AggregatesDeferred = false;
                if (result.Outcome == SubmissionOutcome.Cancelled || result.Outcome == SubmissionOutcome.Stopped)
                    submission.LocalFailure = Common.MessageStatus.timeout;
                else if (result.Outcome == SubmissionOutcome.Failed)
                    submission.LocalFailure = Common.MessageStatus.generic_error;
                PublishSubmissionStatus(submission);
                lock (deliveryTrackingStore.SyncRoot)
                    if (submission.TrackDelivery && submission.Parts.Any(part => part.Status.HasValue)) PublishDeliveryStatus(submission);
            }
        }

        private async Task WaitForRateAsync(CancellationToken token)
        {
            if (submissionOptions.PdusPerSecond == 0) return;
            await rateLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                long now = Stopwatch.GetTimestamp();
                while (nextPduAt > now)
                {
                    // Task.Delay has millisecond resolution; round up and recheck to avoid sending early.
                    var remaining = RuntimeCompatibility.StopwatchTicksToTimeSpan(nextPduAt - now);
                    await Task.Delay(Math.Max(1, (int)Math.Ceiling(remaining.TotalMilliseconds)), token).ConfigureAwait(false);
                    now = Stopwatch.GetTimestamp();
                }
                nextPduAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / submissionOptions.PdusPerSecond;
            }
            finally { rateLock.Release(); }
        }

        private async Task ExpireAsyncPartAsync(uint sequence, AsyncPart expected)
        {
            try { await expected.Completion.Task.WaitAsync(TimeSpan.FromMilliseconds(timeout)).ConfigureAwait(false); }
            catch (TimeoutException) { }
            lock (SessionLock)
                if (!expected.Completion.Task.IsCompleted && asyncParts.TryGetValue(sequence, out var current) && ReferenceEquals(current, expected) &&
                    submitted_messages.TryGetValue(sequence, out var tracked))
                {
                    ExpireSubmission(tracked.submission);
                }
        }

        private void CompleteAsyncPart(uint sequence, MessagePartStatus result)
        {
            if (!asyncParts.Remove(sequence, out var part)) return;
            if (part.HasSlot) { Measure(SmppDiagnostics.InFlight, -1); pduSlots.Release(); }
            part.Completion.TrySetResult(result);
        }

        private void StopSubmissionQueue()
        {
            draining = true;
            sendQueue?.Writer.TryComplete();
            _ = submissionShutdown.CancelAsync();
        }

        /// <summary>Rejects new work, drains queued submissions and acknowledgements, then unbinds. Cancellation forces transport cleanup.</summary>
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            Task worker;
            lock (SessionLock) { draining = true; sendQueue?.Writer.TryComplete(); worker = sendWorker ?? Task.CompletedTask; }
            try
            {
                await worker.WaitAsync(cancellationToken).ConfigureAwait(false);
                while (true)
                {
                    lock (SessionLock)
                        if (submitted_messages.Count == 0 && pendingAcknowledgements == 0 && Volatile.Read(ref activeAcceptanceHandlers) == 0) break;
                    await Task.Delay(20, cancellationToken).ConfigureAwait(false);
                }
            }
            finally { Quit(); }
        }
        public void Dispose() { Quit(); enquire_link_timer.Dispose(); timeout_timer.Dispose(); GC.SuppressFinalize(this); }
        public async ValueTask DisposeAsync()
        {
            using var deadline = new CancellationTokenSource(Math.Max(1, timeout));
            try { await StopAsync(deadline.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            finally { Dispose(); }
        }
    }
}
