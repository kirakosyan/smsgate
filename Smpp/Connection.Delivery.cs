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
            internal DateTime TrackedAt;
        }

        private readonly Dictionary<string, DeliveryPart> deliveryParts = new Dictionary<string, DeliveryPart>();
        private readonly Dictionary<string, (DeliveryReceipt Receipt, DateTime ReceivedAt)> earlyReceipts =
            new Dictionary<string, (DeliveryReceipt, DateTime)>();
        private readonly Dictionary<string, DateTime> ambiguousReceiptIds = new Dictionary<string, DateTime>();
        private TimeSpan deliveryTrackingLifetime = TimeSpan.FromHours(24);
        private int maxTrackedDeliveryParts = 10000;

        /// <summary>How long provider IDs and unmatched early receipts are retained across reconnects.</summary>
        public TimeSpan DeliveryTrackingLifetime
        {
            get => deliveryTrackingLifetime;
            set
            {
                if (value <= TimeSpan.Zero)
                    throw new ArgumentOutOfRangeException(nameof(value));
                deliveryTrackingLifetime = value;
            }
        }

        /// <summary>Maximum retained provider IDs, and separately the maximum unmatched early receipts.</summary>
        public int MaxTrackedDeliveryParts
        {
            get => maxTrackedDeliveryParts;
            set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); maxTrackedDeliveryParts = value; }
        }

        private void ReportPartResponse(uint sequence, submitted_message message, uint? commandStatus, string providerId)
        {
            var submission = message.submission;
            int index = (int)(sequence - submission.FirstSequence);
            var part = submission.Parts[index];
            part.ProviderId = providerId;
            var status = commandStatus == 0 ? Common.MessageStatus.sent :
                commandStatus == null ? Common.MessageStatus.timeout : Common.MessageStatus.rejected;
            var result = new MessagePartStatus(channel_name, message.message_id, sequence,
                index + 1, submission.Parts.Length, providerId, commandStatus, status);
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
            if (deliveryParts.TryGetValue(providerId, out var previous) && !ReferenceEquals(part, previous))
            {
                deliveryParts.Remove(providerId);
                if (ambiguousReceiptIds.Count >= MaxTrackedDeliveryParts)
                    ambiguousReceiptIds.Remove(ambiguousReceiptIds.MinBy(pair => pair.Value).Key);
                ambiguousReceiptIds[providerId] = DateTime.UtcNow;
                previous.Status = part.Status = Common.MessageStatus.generic_error;
                previous.Final = part.Final = true;
                PublishDeliveryStatus(previous.Submission);
                PublishDeliveryStatus(submission);
                Notify(() => Events.LogEvent(Smpp.Events.LogEvent.Level.Error, channel_name + " - Ambiguous provider message ID: " + providerId));
                return;
            }
            while (deliveryParts.Count >= MaxTrackedDeliveryParts)
                ExpireDeliveryPart(deliveryParts.MinBy(pair => pair.Value.TrackedAt).Key);
            part.TrackedAt = DateTime.UtcNow;
            deliveryParts[providerId] = part;
            if (earlyReceipts.Remove(providerId, out var early))
                UpdateTrackedReceipt(part, early.Receipt);
        }

        private void ReportReceipt(DeliveryReceipt receipt)
        {
            ExpireDeliveryTracking(DateTime.UtcNow);
            if (deliveryParts.TryGetValue(receipt.ProviderMessageId, out var part))
            {
                receipt = receipt with
                {
                    MessageId = part.Submission.MessageId, PartNumber = part.Number,
                    PartCount = part.Submission.Parts.Length
                };
                UpdateTrackedReceipt(part, receipt);
            }
            else if (!ambiguousReceiptIds.ContainsKey(receipt.ProviderMessageId))
            {
                if (!earlyReceipts.TryGetValue(receipt.ProviderMessageId, out var previous) || !previous.Receipt.IsFinal)
                {
                    if (earlyReceipts.Count >= MaxTrackedDeliveryParts)
                        earlyReceipts.Remove(earlyReceipts.MinBy(pair => pair.Value.ReceivedAt).Key);
                    earlyReceipts[receipt.ProviderMessageId] = (receipt, DateTime.UtcNow);
                }
            }
            var notification = receipt;
            Notify(() => Events.OnDeliveryReceipt(notification));
            Notify(() => Events.LogMessageDeliverReportEvent(notification.ProviderMessageId, notification.Status));
        }

        private void UpdateTrackedReceipt(DeliveryPart part, DeliveryReceipt receipt)
        {
            if (part.Final)
                return;
            part.Status = receipt.Status;
            part.Final = receipt.IsFinal;
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
            var snapshot = Array.AsReadOnly(submission.Parts.Select(part =>
                new DeliveryPartStatus(part.Number, part.ProviderId, part.Status)).ToArray());
            var result = new MessageDeliveryStatus(channel_name, submission.MessageId, status, complete, snapshot);
            Notify(() => Events.OnDeliveryStatus(result));
        }

        private void ExpireDeliveryPart(string providerId)
        {
            if (!deliveryParts.Remove(providerId, out var part) || part.Final)
                return;
            part.Status = Common.MessageStatus.timeout;
            part.Final = true;
            PublishDeliveryStatus(part.Submission);
        }

        private void ExpireDeliveryTracking(DateTime now)
        {
            foreach (string id in deliveryParts.Where(pair => now - pair.Value.TrackedAt >= DeliveryTrackingLifetime).Select(pair => pair.Key).ToArray())
                ExpireDeliveryPart(id);
            foreach (string id in earlyReceipts.Where(pair => now - pair.Value.ReceivedAt >= DeliveryTrackingLifetime).Select(pair => pair.Key).ToArray())
                earlyReceipts.Remove(id);
            foreach (string id in ambiguousReceiptIds.Where(pair => now - pair.Value >= DeliveryTrackingLifetime).Select(pair => pair.Key).ToArray())
                ambiguousReceiptIds.Remove(id);
        }

        private void ClearDeliveryTracking()
        {
            foreach (string id in deliveryParts.Keys.ToArray())
                ExpireDeliveryPart(id);
            earlyReceipts.Clear();
            ambiguousReceiptIds.Clear();
        }
    }
}
