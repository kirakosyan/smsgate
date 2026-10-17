using System;
using System.Collections.Generic;

namespace Smpp
{
    public sealed record SubmissionOptions
    {
        public int QueueCapacity { get; init; } = 1000;
        public int MaxInFlightPdus { get; init; } = 10;
        /// <summary>Zero disables rate limiting. Counts segments, not logical messages.</summary>
        public int PdusPerSecond { get; init; }
        public int MaxThrottleRetries { get; init; }
        public TimeSpan ThrottleRetryDelay { get; init; } = TimeSpan.FromSeconds(1);
        internal void Validate()
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(QueueCapacity);
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxInFlightPdus);
            ArgumentOutOfRangeException.ThrowIfNegative(PdusPerSecond);
            if (MaxThrottleRetries < 0 || MaxThrottleRetries > 10) throw new ArgumentOutOfRangeException(nameof(MaxThrottleRetries));
            if (ThrottleRetryDelay <= TimeSpan.Zero || ThrottleRetryDelay > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(ThrottleRetryDelay));
        }
    }
    public sealed record OutboundMessage(int MessageId, string Sender, string Recipient, string Body,
        string BodyFormat = "auto", bool RequestDeliveryReport = true);
    public enum SubmissionOutcome { Accepted, Rejected, Unknown, QueueFull, NotConnected, Cancelled, Stopped, Failed }
    /// <summary>Submission acknowledgements, not handset delivery. Null statuses are unknown and must not be blindly retried.</summary>
    public sealed record SubmissionResult(int MessageId, SubmissionOutcome Outcome,
        IReadOnlyList<MessagePartStatus> Parts, int Attempts = 1)
    {
        public bool Accepted => Outcome == SubmissionOutcome.Accepted;
        public bool MayHaveBeenAccepted { get; init; }
        public string ErrorMessage { get; init; }
    }
    public sealed record BindResult(bool Bound, uint? CommandStatus);
}
