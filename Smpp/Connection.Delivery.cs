using System;
using System.Collections.Generic;
using System.Linq;

namespace Smpp
{
    public abstract partial class Connection
    {
        internal sealed class DeliveryPart
        {
            internal Submission Submission;
            internal int Number;
            internal string ProviderId = "";
            internal Common.MessageStatus? Status;
            internal bool Final;
            internal long AcceptedAt;
        }

        internal sealed class DeliveryGroup
        {
            internal readonly List<DeliveryPart> Parts = new();
            internal DeliveryReceipt LastReceipt;
        }
        private DeliveryTrackingStore deliveryTrackingStore = new();
        private bool deliveryTrackingStarted;
        /// <summary>Share one store between the transmitter and receiver for the same provider account. Set before traffic starts.</summary>
        public DeliveryTrackingStore DeliveryTrackingStore
        {
            get => deliveryTrackingStore;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                lock (SessionLock)
                {
                    if (deliveryTrackingStarted || connected) throw new InvalidOperationException("Set the delivery store before connecting or processing traffic.");
                    deliveryTrackingStore = value;
                }
            }
        }
        private TimedCache<DeliveryGroup> deliveryParts => deliveryTrackingStore.Parts;
        private TimedCache<DeliveryReceipt> earlyReceipts => deliveryTrackingStore.EarlyReceipts;
        private TimedCache<bool> ambiguousReceiptIds => deliveryTrackingStore.AmbiguousIds;
        /// <summary>Set true for providers treating ACCEPTD as terminal. Default permits later handset delivery.</summary>
        public bool AcceptedReceiptsAreFinal { get; set; }
        private bool IsFinalReceipt(DeliveryReceipt receipt) => receipt.IsFinal ||
            (AcceptedReceiptsAreFinal && receipt.MessageState == 6);
        public TimeSpan DeliveryTrackingLifetime { get => deliveryTrackingStore.Lifetime; set => deliveryTrackingStore.Lifetime = value; }
        public int MaxTrackedDeliveryParts { get => deliveryTrackingStore.Capacity; set => deliveryTrackingStore.Capacity = value; }

        private void ReportPartResponse(uint sequence, submitted_message message, uint? commandStatus, string providerId)
        {
            lock (deliveryTrackingStore.SyncRoot)
            {
                deliveryTrackingStarted = true;
                var submission = message.submission;
                int index = (int)(sequence - submission.FirstSequence);
                var part = submission.Parts[index];
                part.ProviderId = providerId;
                if (commandStatus == 0) part.AcceptedAt = System.Diagnostics.Stopwatch.GetTimestamp();
                var status = commandStatus == 0 ? Common.MessageStatus.sent :
                    commandStatus == null ? Common.MessageStatus.timeout : Common.MessageStatus.rejected;
                var result = new MessagePartStatus(channel_name, message.message_id, sequence,
                    index + 1, submission.Parts.Length, providerId, commandStatus, status);
                CompleteAsyncPart(sequence, result);
                SmppDiagnostics.Responses.Add(1, new KeyValuePair<string, object>("status", commandStatus?.ToString() ?? "unknown"));
                double? elapsed = null;
                if (message.submitted_time != DateTime.MaxValue)
                {
                    elapsed = Math.Max(0, (DateTime.UtcNow - message.submitted_time).TotalMilliseconds);
                    SmppDiagnostics.Latency.Record(elapsed.Value);
                }
                Diagnose("pdu.response", sequence, commandStatus, elapsed);
                Notify(() => Events.OnPartStatus(result));
                if (!submission.TrackDelivery)
                    return;
                if (commandStatus != 0)
                {
                    part.Status = status;
                    part.Final = true;
                    PublishDeliveryStatus(submission);
                    return;
                }
                if (string.IsNullOrEmpty(providerId))
                {
                    part.Status = Common.MessageStatus.generic_error;
                    part.Final = true;
                    PublishDeliveryStatus(submission);
                    return;
                }
                ExpireDeliveryTracking(DateTime.UtcNow);
                if (ambiguousReceiptIds.ContainsKey(providerId))
                {
                    part.Status = Common.MessageStatus.generic_error;
                    part.Final = true;
                    PublishDeliveryStatus(submission);
                    return;
                }
                if (deliveryParts.TryGetValue(providerId, out var group))
                {
                    if (!ReferenceEquals(group.Parts[0].Submission, submission))
                    {
                        deliveryParts.Remove(providerId, out _);
                        ambiguousReceiptIds.Set(providerId, true);
                        ambiguousReceiptIds.Trim(MaxTrackedDeliveryParts, DateTime.MinValue);
                        foreach (var previous in group.Parts)
                        {
                            previous.Status = Common.MessageStatus.generic_error;
                            previous.Final = true;
                        }
                        part.Status = Common.MessageStatus.generic_error;
                        part.Final = true;
                        group.Parts[0].Submission.Owner.PublishDeliveryStatus(group.Parts[0].Submission);
                        PublishDeliveryStatus(submission);
                        Notify(() => Events.LogEvent(Smpp.Events.LogEvent.Level.Error, channel_name + " - Ambiguous provider message ID: " + providerId));
                        return;
                    }
                }
                else
                {
                    group = new DeliveryGroup();
                    deliveryParts.Set(providerId, group);
                    deliveryParts.Trim(MaxTrackedDeliveryParts, DateTime.MinValue, ExpireDeliveryGroup);
                }
                group.Parts.Add(part);
                if (earlyReceipts.Remove(providerId, out var early)) group.LastReceipt = early;
                if (group.LastReceipt != null) part.Submission.Owner.UpdateTrackedReceipt(part, group.LastReceipt);
            }
        }

        private void ReportReceipt(DeliveryReceipt receipt)
        {
            lock (deliveryTrackingStore.SyncRoot)
            {
                deliveryTrackingStarted = true;
                SmppDiagnostics.Receipts.Add(1, new KeyValuePair<string, object>("state", receipt.MessageState));
                ExpireDeliveryTracking(DateTime.UtcNow);
                if (deliveryParts.TryGetValue(receipt.ProviderMessageId, out var group))
                {
                    var part = group.Parts[0];
                    receipt = receipt with
                    {
                        MessageId = part.Submission.MessageId,
                        PartNumber = group.Parts.Count == 1 ? part.Number : null,
                        PartCount = part.Submission.Parts.Length
                    };
                    if (group.LastReceipt == null || !part.Submission.Owner.IsFinalReceipt(group.LastReceipt)) group.LastReceipt = receipt;
                    foreach (var tracked in group.Parts) tracked.Submission.Owner.UpdateTrackedReceipt(tracked, receipt);
                }
                else if (!ambiguousReceiptIds.ContainsKey(receipt.ProviderMessageId))
                {
                    if (!earlyReceipts.TryGetValue(receipt.ProviderMessageId, out var previous) || !previous.IsFinal)
                    {
                        earlyReceipts.Set(receipt.ProviderMessageId, receipt);
                        earlyReceipts.Trim(MaxTrackedDeliveryParts, DateTime.MinValue);
                    }
                }
                var notification = receipt;
                Notify(() => Events.OnDeliveryReceipt(notification));
                Notify(() => Events.LogMessageDeliverReportEvent(notification.ProviderMessageId, notification.Status));
            }
        }

        private void UpdateTrackedReceipt(DeliveryPart part, DeliveryReceipt receipt)
        {
            if (part.Final) return;
            part.Status = receipt.Status;
            part.Final = IsFinalReceipt(receipt);
            if (part.Final && part.AcceptedAt != 0)
                SmppDiagnostics.DeliveryLatency.Record(System.Diagnostics.Stopwatch.GetElapsedTime(part.AcceptedAt).TotalMilliseconds);
            PublishDeliveryStatus(part.Submission);
        }

        private void PublishDeliveryStatus(Submission submission)
        {
            if (submission.DeliveryCompleted)
                return;
            var failed = submission.Parts.FirstOrDefault(part => part.Final &&
                part.Status != Common.MessageStatus.delivered_ACK_received && part.Status != Common.MessageStatus.received_routed);
            bool complete = failed != null || submission.Parts.All(part => part.Final);
            var status = failed?.Status ?? (complete
                ? submission.Parts.All(part => part.Status == Common.MessageStatus.delivered_ACK_received)
                    ? Common.MessageStatus.delivered_ACK_received : Common.MessageStatus.received_routed
                : Common.MessageStatus.submitted_waiting_for_ACK);
            submission.DeliveryCompleted = complete;
            if (complete) SmppDiagnostics.Delivered.Add(1, new KeyValuePair<string, object>("status", status.ToString()));
            var snapshot = Array.AsReadOnly(submission.Parts.Select(part =>
                new DeliveryPartStatus(part.Number, part.ProviderId, part.Status)).ToArray());
            var owner = submission.Owner;
            var result = new MessageDeliveryStatus(owner.channel_name, submission.MessageId, status, complete, snapshot);
            owner.Notify(() => owner.Events.OnDeliveryStatus(result));
        }

        internal static void ExpireDeliveryGroup(DeliveryGroup group)
        {
            foreach (var part in group.Parts)
            {
                if (part.Final) continue;
                part.Status = Common.MessageStatus.timeout;
                part.Final = true;
                part.Submission.Owner.PublishDeliveryStatus(part.Submission);
            }
        }

        private void ExpireDeliveryTracking(DateTime now)
        {
            lock (deliveryTrackingStore.SyncRoot)
            {
                var cutoff = now - DeliveryTrackingLifetime;
                deliveryParts.Trim(MaxTrackedDeliveryParts, cutoff, ExpireDeliveryGroup);
                earlyReceipts.Trim(MaxTrackedDeliveryParts, cutoff);
                ambiguousReceiptIds.Trim(MaxTrackedDeliveryParts, cutoff);
            }
        }

        private void ClearDeliveryTracking()
        {
            lock (deliveryTrackingStore.SyncRoot)
            {
                deliveryParts.RemoveWhere(group => ReferenceEquals(group.Parts[0].Submission.Owner, this), ExpireDeliveryGroup);
            }
        }
    }
}
