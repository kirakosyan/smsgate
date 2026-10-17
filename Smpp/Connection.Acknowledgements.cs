using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Smpp.Events;

namespace Smpp
{
    public abstract partial class Connection
    {
        private CancellationTokenSource sessionCancellation;
        private readonly HashSet<object> pendingAcknowledgements = new HashSet<object>();
        private int maxPendingAcknowledgements = 100;
        private int acknowledgementTimeout = 10000;
        private int activeAcceptanceHandlers;

        private static async Task CancelSessionAsync(CancellationTokenSource cancellation)
        {
            try { await cancellation.CancelAsync().ConfigureAwait(false); }
            catch (Exception) { } // Application cancellation callbacks cannot prevent transport cleanup.
            finally { cancellation.Dispose(); }
        }

        /// <summary>Optional per-PDU acceptance handler. Persist the PDU before returning Accept. Null retains automatic acknowledgements.</summary>
        public Func<InboundMessage, CancellationToken, Task<MessageAcknowledgement>> InboundMessageHandler { get; set; }

        public int MaxPendingAcknowledgements
        {
            get => maxPendingAcknowledgements;
            set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); maxPendingAcknowledgements = value; }
        }

        /// <summary>Application acknowledgement deadline in milliseconds. Timeout or handler failure returns ESME_RSYSERR.</summary>
        public int AcknowledgementTimeout
        {
            get => acknowledgementTimeout;
            set { ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value); acknowledgementTimeout = value; }
        }

        private void DispatchInbound(Pdu request, string sender, string recipient, string body, string format,
            int registeredDelivery, Common.MultipartMessage? multipart, DeliveryReceipt receipt,
            Func<MessageAcknowledgement, string> encodeResponse, Action<string> accepted)
        {
            string generatedId = Guid.NewGuid().ToString("n");
            var handler = InboundMessageHandler;
            if (handler == null)
            {
                SendAcknowledgement(request, encodeResponse, MessageAcknowledgement.Accept(generatedId));
                accepted(generatedId);
                return;
            }
            if (pendingAcknowledgements.Count >= MaxPendingAcknowledgements ||
                Volatile.Read(ref activeAcceptanceHandlers) >= MaxPendingAcknowledgements)
            {
                SendAcknowledgement(request, encodeResponse, MessageAcknowledgement.Reject(0x58)); // ESME_RTHROTTLED
                return;
            }
            var parameters = new ReadOnlyDictionary<string, string>(request.tlv.ToDictionary(pair => pair.Key, pair => pair.Value.ToString()));
            var message = new InboundMessage(channel_name, generatedId, (Common.CommandId)request.command_id,
                request.sequence_number, sender, recipient, body, format, registeredDelivery,
                multipart?.reference, multipart?.part_num, multipart?.num_of_parts, parameters, receipt);
            object work = new object();
            pendingAcknowledgements.Add(work);
            Interlocked.Increment(ref activeAcceptanceHandlers);
            int generation = sessionGeneration;
            var token = sessionCancellation?.Token ?? CancellationToken.None;
            // Start the deadline at dispatch, even if the thread pool is busy. Only application work is queued.
            _ = ProcessInboundAsync(work, generation, token, handler, request, message, encodeResponse, accepted);
        }

        private void SendAcknowledgement(Pdu request, Func<MessageAcknowledgement, string> encodeResponse, MessageAcknowledgement acknowledgement)
        {
            Common.command_id.TryGetValue(request.command_id | 0x80000000, out var name);
            Notify(() => Events.LogChannelEvent(channel_name, "Sending [" + name + "]"));
            SendPDU(encodeResponse(acknowledgement));
        }

        private async Task ProcessInboundAsync(object work, int generation, CancellationToken sessionToken,
            Func<InboundMessage, CancellationToken, Task<MessageAcknowledgement>> handler, Pdu request, InboundMessage message,
            Func<MessageAcknowledgement, string> encodeResponse, Action<string> accepted)
        {
            MessageAcknowledgement response;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(sessionToken);
            deadline.CancelAfter(AcknowledgementTimeout);
            try
            {
                // The callback can do synchronous work before returning its task. It never runs on a protocol reader.
                var processing = Task.Run(async () =>
                {
                    try
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        return await handler(message, deadline.Token).ConfigureAwait(false);
                    }
                    finally { Interlocked.Decrement(ref activeAcceptanceHandlers); }
                });
                response = await processing.WaitAsync(deadline.Token).ConfigureAwait(false);
                if (response.CommandStatus == 0)
                {
                    string id = response.MessageId ?? message.MessageId;
                    if (id.Length > 64 || id.Any(value => value == 0 || value > 127))
                        throw new ArgumentException("Acknowledgement IDs must be ASCII, at most 64 characters, without NUL.");
                    response = response with { MessageId = id };
                }
                else
                    response = response with { MessageId = "" };
            }
            catch (Exception ex)
            {
                response = MessageAcknowledgement.Reject(8); // ESME_RSYSERR
                if (!sessionToken.IsCancellationRequested)
                    Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Inbound acceptance failed: " + ex.Message));
            }
            lock (SessionLock)
            {
                if (!pendingAcknowledgements.Remove(work) || stopped || sessionGeneration != generation || sessionToken.IsCancellationRequested)
                    return;
                if (deadline.IsCancellationRequested)
                    response = MessageAcknowledgement.Reject(8);
                SendAcknowledgement(request, encodeResponse, response);
                if (response.CommandStatus == 0)
                    accepted(response.MessageId);
            }
        }
    }
}
