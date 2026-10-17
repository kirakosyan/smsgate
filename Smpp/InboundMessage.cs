using System;
using System.Collections.Generic;

namespace Smpp
{
    /// <summary>An incoming PDU, before acknowledgement. Multipart bodies are individual segments at this stage.</summary>
    public sealed record InboundMessage(string ChannelName, string MessageId, Common.CommandId Command,
        uint SequenceNumber, string Sender, string Recipient, string Body, string BodyFormat,
        int RegisteredDelivery, int? MultipartReference, int? PartNumber, int? PartCount,
        IReadOnlyDictionary<string, string> OptionalParameters, DeliveryReceipt Receipt);

    /// <summary>The SMPP response to an inbound PDU. Zero accepts; a nonzero command status rejects.</summary>
    public readonly record struct MessageAcknowledgement(uint CommandStatus, string MessageId = null)
    {
        public static MessageAcknowledgement Accept(string messageId = null) => new MessageAcknowledgement(0, messageId);

        public static MessageAcknowledgement Reject(uint commandStatus)
        {
            if (commandStatus == 0)
                throw new ArgumentOutOfRangeException(nameof(commandStatus), "Rejections require a nonzero SMPP status.");
            return new MessageAcknowledgement(commandStatus);
        }
    }
}
