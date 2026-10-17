using System.Collections.Generic;

namespace Smpp
{
    /// <summary>A response or transport timeout for one submitted PDU. A null command status means no response arrived.</summary>
    public sealed record MessagePartStatus(string ChannelName, int MessageId, uint SequenceNumber,
        int PartNumber, int PartCount, string ProviderMessageId, uint? CommandStatus, Common.MessageStatus Status);

    /// <summary>A provider delivery receipt. Correlation can be absent when the receipt precedes its submit response.</summary>
    public sealed record DeliveryReceipt(string ChannelName, string ProviderMessageId, Common.MessageStatus Status,
        byte MessageState, string ErrorCode, string NetworkErrorCode, int? MessageId = null,
        int? PartNumber = null, int? PartCount = null)
    {
        public bool IsFinal => MessageState != 1;
    }

    /// <summary>The delivery state of a multipart message, with a snapshot of all its parts.</summary>
    public sealed record MessageDeliveryStatus(string ChannelName, int MessageId, Common.MessageStatus Status,
        bool IsComplete, IReadOnlyList<DeliveryPartStatus> Parts);

    public sealed record DeliveryPartStatus(int PartNumber, string ProviderMessageId, Common.MessageStatus? Status);
}
