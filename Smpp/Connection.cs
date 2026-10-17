using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;

namespace Smpp
{
    /// <summary>
    /// Base abstract class for server and client connections
    /// </summary>
    public abstract partial class Connection
    {
        protected SortedList<uint, DateTime> commands_queue;
        protected SortedList<uint, submitted_message> submitted_messages;
        protected List<Common.MultipartMessage> multipart_messages;

        /// <summary>
        /// Query of submitted messages
        /// </summary>
        protected struct submitted_message
        {
            public int message_id;
            public DateTime submitted_time;
            public bool registered_delivery;
            internal Submission submission;
        }

        internal sealed class Submission
        {
            internal int Remaining;
            internal uint FirstSequence;
            internal string ResponseId = "";
            internal bool Completed;
            internal DeliveryPart[] Parts;
            internal bool DeliveryCompleted;
            internal bool TrackDelivery;
            internal int MessageId;
        }

        protected TcpClient tcp_client;
        protected NetworkStream tcp_stream;
        private System.IO.Stream sessionStream;

        /// <summary>
        /// Incomplete frame retained between socket reads.
        /// </summary>
        string _partial_pdu = "";

        /// <summary>
        /// True only after a successful bind, until the session ends.
        /// </summary>
        public volatile bool connected;

        /// <summary>
        /// Indicates that channel is server and should listen for connections
        /// </summary>
        internal bool is_server;

        protected uint sequence_number;
        protected byte multipart_sequence_number;

        #region SMPP Settings

        /// <summary>
        /// Indicates that channel can receive messages
        /// </summary>
        public bool direction_in;

        /// <summary>
        /// Indicates that channel can send messages
        /// </summary>
        public bool direction_out;

        /// <summary>
        /// Host name or IP
        /// </summary>
        public string host;

        /// <summary>
        /// SMPP port, by default 2775
        /// </summary>
        public int port;

        /// <summary>
        /// SystemId
        /// </summary>
        public string system_id;

        /// <summary>
        /// Password
        /// </summary>
        public string password;

        /// <summary>
        /// System type, empty by default
        /// </summary>
        public string system_type = string.Empty;

        protected string source_address;

        /// <summary>
        /// Source TON, 0 by default
        /// 1 - International number
        /// 2 - National number
        /// 3 - Network specific number
        /// 4 - Subscriber number
        /// 5 - Abbreviated number
        /// </summary>
        public int source_ton = 0;

        /// <summary>
        /// Source NPI, 0 by default
        /// 1 - ISDN
        /// 2 - Reserved
        /// 3 - Data (X.121)
        /// 4 - Telex (F.69)
        /// 5 - Reserved
        /// 6 - Land mobile (E.212)
        /// 7 - Reserved
        /// 8 - National
        /// 9 - Private
        /// 10 - ERMES
        /// 18 - WAP client ID
        /// </summary>
        public int source_npi = 0;

        /// <summary>
        /// Destination TON, 0 by default
        /// 1 - International number
        /// 2 - National number
        /// 3 - Network specific number
        /// 4 - Subscriber number
        /// 5 - Abbreviated number
        /// </summary>
        public int destination_ton = 0;

        /// <summary>
        /// Destination NPI, 0 by default
        /// 1 - ISDN
        /// 2 - Reserved
        /// 3 - Data (X.121)
        /// 4 - Telex (F.69)
        /// 5 - Reserved
        /// 6 - Land mobile (E.212)
        /// 7 - Reserved
        /// 8 - National
        /// 9 - Private
        /// 10 - ERMES
        /// 18 - WAP client ID
        /// </summary>
        public int destination_npi;

        /// <summary>
        /// Indicates that sent messages require delivery reports, 1 by default
        /// </summary>
        public int registered_delivery = 1;

        /// <summary>
        /// Indicates the way large message will be sent, 1 by default
        /// 1 - Multipart
        /// 2 - Payload
        /// 3 - truncate 70/160
        /// 4 - fail
        /// </summary>
        public int large_message = 1;

        /// <summary>
        /// Indicates that entire PDU will be logged
        /// </summary>
        public bool debug;

        /// <summary>
        /// Delay in milliseconds for multipart messages, default 100
        /// </summary>
        public int multypart_delay = 100;

        /// <summary>
        /// Use deliver_sm for sending messages, false by default, submit_sm will be used
        /// </summary>
        public bool use_deliver_sm = false;

        /// <summary>
        /// Split limit for ASCII messages, 153 by default
        /// 7 characters should be reserved for multipart metadata
        /// </summary>
        public int split_base_ascii = 153;

        protected int queue_resend_delay = 200;

        private int _keep_alive = 60000;
        private int _timeout = 10000;

        /// <summary>
        /// Interval in milliseconds to send enquire link, 1 minute by default
        /// </summary>
        public int keep_alive
        {
            get { return _keep_alive; }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                _keep_alive = value;
                if (enquire_link_timer != null)
                {
                    enquire_link_timer.Interval = value;
                }
            }
        }

        /// <summary>
        /// Timeout in milliseconds if remote host does not answer, 10 seconds by default
        /// </summary>
        public int timeout
        {
            get { return _timeout; }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
                _timeout = value;
                if (timeout_timer != null)
                {
                    timeout_timer.Interval = value;
                }
            }
        }

        /// <summary>
        /// The delay to reconnect after timeout or connection problem
        /// </summary>
        public int reconnectDelay = 10000;

        /// <summary>
        /// Indicates that messages are sent in 8bit format, false by default
        /// </summary>
        public bool use8bit = false;

        /// <summary>Decode inbound data_coding 0 as GSM-7. Configure this to match the provider's default alphabet.</summary>
        public bool UseGsm7 { get; set; }

        /// <summary>
        /// maximum number of threads while sending, 0 by default
        /// 0 = no limit
        /// </summary>
        protected int threads_count;

        #endregion

        protected string channel_name;

        /// <summary>
        /// Maximum SMS per second, 0 by default
        /// 0 - no limit
        /// </summary>
        protected int max_threads = 0;

        protected int sent_during_last_second;
        protected int last_second;

        #region Timers
        protected System.Timers.Timer enquire_link_timer;
        protected System.Timers.Timer timeout_timer;
        #endregion

        protected GateEvents Events;

        protected Connection(string channelName, GateEvents events)
        {
            channel_name = channelName;
            Events = events ?? throw new ArgumentNullException(nameof(events));
            sequence_number = 1;
            connected = false;

            // Make bidirectional by default
            direction_in = true;
            direction_out = true;

            enquire_link_timer = new System.Timers.Timer();
            enquire_link_timer.Interval = keep_alive;
            enquire_link_timer.Elapsed += new ElapsedEventHandler(enquire_link_timer_Elapsed);

            timeout_timer = new System.Timers.Timer();
            timeout_timer.Interval = timeout;
            timeout_timer.Elapsed += new ElapsedEventHandler(timeout_timer_Elapsed);

            commands_queue = new SortedList<uint, DateTime>();
            submitted_messages = new SortedList<uint, submitted_message>();
            multipart_messages = new List<Common.MultipartMessage>();

            multipart_sequence_number = 1;
        }


        protected readonly object SessionLock = new object();
        protected bool stopped;
        private CancellationTokenSource reconnectCancellation;
        private uint? pendingBindSequence;
        private uint pendingBindResponse;
        private int sessionGeneration;
        private static readonly TimeSpan MultipartLifetime = TimeSpan.FromMinutes(5);
        private System.IO.Stream receivingStream;
        private bool invalidFraming;
        private readonly Queue<Action> notifications = new Queue<Action>();
        private bool dispatchingNotifications;

        // Serialize application notifications independently of protocol processing. No callback
        // executes under SessionLock, and a slow subscriber cannot stall reads or keepalives.
        protected void Notify(Action notification)
        {
            lock (SessionLock)
            {
                notifications.Enqueue(notification);
                if (dispatchingNotifications)
                    return;
                dispatchingNotifications = true;
                _ = Task.Run(DispatchNotifications);
            }
        }

        private void DispatchNotifications()
        {
            while (true)
            {
                Action notification;
                lock (SessionLock)
                {
                    if (notifications.Count == 0)
                    {
                        dispatchingNotifications = false;
                        return;
                    }
                    notification = notifications.Dequeue();
                }
                try { notification(); }
                catch (Exception ex)
                {
                    try { Events.LogEvent(LogEvent.Level.Error, channel_name + " - Application callback failed: " + ex.Message); }
                    catch (Exception) { } // A failing error subscriber must not stop dispatch.
                }
            }
        }

        internal Task DrainNotificationsAsync()
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Notify(() => completion.SetResult());
            return completion.Task;
        }

        protected void ScheduleReconnect() => ScheduleReconnect(reconnectDelay);

        protected virtual void ScheduleReconnect(int delayMilliseconds)
        {
            lock (SessionLock)
            {
                if (is_server || stopped || reconnectCancellation != null)
                    return;
                var cancellation = new CancellationTokenSource();
                reconnectCancellation = cancellation;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(Math.Max(0, delayMilliseconds), cancellation.Token).ConfigureAwait(false);
                        Client client;
                        lock (SessionLock)
                        {
                            if (stopped || cancellation.IsCancellationRequested ||
                                !ReferenceEquals(reconnectCancellation, cancellation))
                                return;
                            reconnectCancellation = null;
                            lock (Gate.Clients)
                                Gate.Clients.TryGetValue(channel_name, out client);
                        }
                        if (ReferenceEquals(client, this))
                            await client.ConnectAsync().ConfigureAwait(false);
                        else
                            Notify(() => Events.LogChannelEvent(channel_name, "Reconnect skipped: client is no longer registered"));
                    }
                    catch (OperationCanceledException) { }
                    finally
                    {
                        cancellation.Dispose();
                    }
                });
            }
        }

        protected void CancelReconnect()
        {
            reconnectCancellation?.Cancel();
            reconnectCancellation = null;
        }

        protected uint NextSequenceNumber()
        {
            uint value = FindAvailableSequenceRange(1);
            sequence_number = value >= 0x7FFFFFFF ? 1 : value + 1;
            return value;
        }

        private uint FindAvailableSequenceRange(int count)
        {
            uint candidate = sequence_number;
            // Each failed range skips at least one occupied sequence, including command IDs.
            for (int attempt = 0; attempt <= submitted_messages.Count + commands_queue.Count; attempt++)
            {
                if (candidate == 0 || (ulong)candidate + (uint)count - 1 > 0x7FFFFFFF)
                    candidate = 1;
                bool available = true;
                for (uint offset = 0; offset < count; offset++)
                {
                    uint sequence = candidate + offset;
                    if (submitted_messages.ContainsKey(sequence) || commands_queue.ContainsKey(sequence))
                    {
                        candidate = sequence + 1;
                        available = false;
                        break;
                    }
                }
                if (available)
                    return candidate;
            }
            throw new InvalidOperationException("No free SMPP sequence range.");
        }

        protected void BeginSession(TcpClient client, bool bound) => BeginSession(client, bound, client.GetStream());

        protected void BeginSession(TcpClient client, bool bound, System.IO.Stream transport)
        {
            tcp_client = client;
            tcp_stream = client.GetStream();
            sessionStream = transport;
            sessionStream.WriteTimeout = timeout;
            sessionCancellation = new CancellationTokenSource();
            connected = bound;
            pendingBindSequence = null;
            _partial_pdu = "";
            invalidFraming = false;
            sessionGeneration++;
            enquire_link_timer.Start();
            timeout_timer.Start();
        }

        protected int SendBind(Pdu bind, Common.CommandId responseId, string name)
        {
            bind.sequence_number = NextSequenceNumber();
            pendingBindSequence = bind.sequence_number;
            pendingBindResponse = (uint)responseId;
            commands_queue.Add(bind.sequence_number, DateTime.UtcNow);
            string pdu = bind.Encode();
            // Bind bodies contain passwords and must not be included in debug logs.
            Notify(() => Events.LogChannelEvent(channel_name, "Sending [" + name + "]"));
            return SendPDU(pdu);
        }

        void enquire_link_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (SessionLock)
            {
                if (!connected || stopped)
                    return;
                var request = new EnquireLink { sequence_number = NextSequenceNumber() };
                commands_queue.Add(request.sequence_number, DateTime.UtcNow);
                string pdu = request.Encode();
                Notify(() => Events.LogChannelEvent(channel_name, "Sending [enquire_link]", debug ? pdu : ""));
                SendPDU(pdu);
            }
        }

        internal void Receive()
        {
            lock (SessionLock)
            {
                if (sessionStream == null || stopped || ReferenceEquals(receivingStream, sessionStream))
                    return;
                var stream = sessionStream;
                receivingStream = stream;
                _ = Task.Run(() => ReceiveLoopAsync(stream));
            }
        }

        private async Task ReceiveLoopAsync(System.IO.Stream stream)
        {
            var buffer = new byte[8192];
            try
            {
                while (true)
                {
                    int count = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    lock (SessionLock)
                    {
                        // A read from a closed socket must never touch its replacement session.
                        if (!ReferenceEquals(sessionStream, stream))
                            return;
                        if (count == 0)
                        {
                            Disconnect(true);
                            return;
                        }
                        MessageHandler(Common.ConvertByteArrayToHexString(buffer, count));
                        if (!ReferenceEquals(sessionStream, stream))
                            return;
                    }
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException || ex is SocketException)
            {
                lock (SessionLock)
                {
                    if (!ReferenceEquals(sessionStream, stream))
                        return;
                    Notify(() => Events.LogChannelEvent(channel_name, "Receive failed: " + ex.Message));
                    Disconnect(true);
                }
            }
        }

        void timeout_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (SessionLock)
            {
                var now = DateTime.UtcNow;
                ExpireDeliveryTracking(now);
                multipart_messages.RemoveAll(part => now - part.submitted_dt >= MultipartLifetime);
                if (stopped || tcp_stream == null)
                    return;
                if (commands_queue.Values.Any(time => (now - time).TotalMilliseconds >= timeout))
                {
                    Notify(() => Events.LogChannelEvent(channel_name, "Command timeout"));
                    Disconnect(true);
                    return;
                }
                var expired = submitted_messages.Values
                    .Where(message => (now - message.submitted_time).TotalMilliseconds >= timeout)
                    .Select(message => message.submission).Distinct().ToArray();
                foreach (var submission in expired)
                {
                    var sequences = submitted_messages.Where(pair => ReferenceEquals(pair.Value.submission, submission))
                        .Select(pair => pair.Key).ToArray();
                    int id = submitted_messages[sequences[0]].message_id;
                    foreach (uint sequence in sequences)
                    {
                        ReportPartResponse(sequence, submitted_messages[sequence], null, "");
                        submitted_messages.Remove(sequence);
                    }
                    if (!submission.Completed)
                    {
                        submission.Completed = true;
                        Notify(() => Events.LogMessageChangeStatusEvent(id, Common.MessageStatus.timeout));
                    }
                }
                // The timer remains active after a healthy tick, including while idle.
            }
        }

        protected void Disconnect(bool reconnect)
        {
            lock (SessionLock)
            {
                connected = false;
                pendingBindSequence = null;
                enquire_link_timer.Stop();
                timeout_timer.Stop();
                var stream = sessionStream;
                var client = tcp_client;
                tcp_stream = null;
                sessionStream = null;
                if (sessionCancellation != null)
                    _ = CancelSessionAsync(sessionCancellation);
                sessionCancellation = null;
                pendingAcknowledgements.Clear();
                tcp_client = null;
                sessionGeneration++;
                stream?.Dispose();
                client?.Dispose();
                commands_queue.Clear();
                _partial_pdu = "";
                // Already-ACKed segments must survive transient disconnects until their lifetime expires.
                var pending = submitted_messages.Values.Where(message => !message.submission.Completed)
                    .Select(message => message.message_id).Distinct().ToArray();
                foreach (var pair in submitted_messages)
                    ReportPartResponse(pair.Key, pair.Value, null, "");
                submitted_messages.Clear();
                if (reconnect)
                    ScheduleReconnect();
                foreach (int id in pending)
                    Notify(() => Events.LogMessageChangeStatusEvent(id, Common.MessageStatus.timeout));
            }
        }

        public int SendPDU(string pdu)
        {
            byte[] bytes = Common.ConvertHexStringToByteArray(pdu);
            lock (SessionLock)
            {
                if (tcp_stream == null)
                    return 1;
                try
                {
                    // Serialize complete frames; in particular, finish unbind_resp before closing.
                    sessionStream.WriteTimeout = timeout;
                    sessionStream.Write(bytes, 0, bytes.Length);
                    return 0;
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException || ex is SocketException)
                {
                    Notify(() => Events.LogChannelEvent(channel_name, "Send failed: " + ex.Message));
                    Disconnect(true);
                    return 502;
                }
            }
        }

        public void MessageHandler(object mess)
        {
            lock (SessionLock)
            {
                if (invalidFraming)
                    return;
                var data = new List<string>();
                _partial_pdu += mess?.ToString();
                int offset = 0;
                while (_partial_pdu.Length - offset >= 8)
                {
                    if (!uint.TryParse(_partial_pdu.AsSpan(offset, 8), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint length) ||
                        length < 16 || length > Common.MAX_PDU_LENGTH)
                    {
                        Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Invalid PDU length"));
                        invalidFraming = true;
                        break;
                    }
                    if (_partial_pdu.Length - offset < length * 2)
                        break;
                    data.Add(_partial_pdu.Substring(offset, (int)length * 2));
                    offset += (int)length * 2;
                }
                _partial_pdu = invalidFraming ? "" : _partial_pdu.Substring(offset);

                int generation = sessionGeneration;
                foreach (string pdu in data)
                {
                    if (generation != sessionGeneration)
                        break;
                    try { PduHandler(pdu); }
                    catch (Exception ex)
                    {
                        // A complete but malformed message still needs an error response.
                        uint command = Convert.ToUInt32(pdu.Substring(8, 8), 16);
                        if (command == (uint)Common.CommandId.submit_sm || command == (uint)Common.CommandId.deliver_sm ||
                            command == (uint)Common.CommandId.data_sm)
                            SendPDU(new GenericNack
                            {
                                sequence_number = Convert.ToUInt32(pdu.Substring(24, 8), 16),
                                command_status = 1 // ESME_RINVMSGLEN
                            }.Encode());
                        Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - PDU handling failed: " + ex.Message));
                    }
                }
                if (invalidFraming && generation == sessionGeneration)
                    Disconnect(true); // TCP has no marker with which to recover a frame boundary.
            }
        }

        private void HandleBindResponse(uint command, uint sequence, uint status)
        {
            if (pendingBindSequence != sequence || pendingBindResponse != command)
                return;
            commands_queue.Remove(sequence);
            pendingBindSequence = null;
            if (status == 0)
            {
                connected = true;
                Notify(() => Events.LogChannelEvent(channel_name, "Bound successfully"));
            }
            else
            {
                Notify(() => Events.LogChannelEvent(channel_name, "Bind rejected: " + status));
                Disconnect(true);
            }
        }

        private void CompleteSubmission(uint sequence, uint status, string messageId)
        {
            if (!submitted_messages.TryGetValue(sequence, out var message))
                return;
            submitted_messages.Remove(sequence);
            var submission = message.submission;
            ReportPartResponse(sequence, message, status, messageId ?? "");
            submission.Remaining--;
            if (sequence == submission.FirstSequence)
                submission.ResponseId = messageId ?? "";
            if (submission.Completed)
                return;
            if (status != 0)
            {
                submission.Completed = true;
                Notify(() => Events.LogMessageChangeStatusEvent(message.message_id, Common.MessageStatus.rejected, messageId ?? ""));
            }
            else if (submission.Remaining == 0)
            {
                submission.Completed = true;
                Notify(() => Events.LogMessageChangeStatusEvent(message.message_id, Common.MessageStatus.sent, submission.ResponseId));
            }
        }

        private bool TryAssemble(Common.MultipartMessage part, out string body)
        {
            body = null;
            var now = DateTime.UtcNow;
            multipart_messages.RemoveAll(value => now - value.submitted_dt >= MultipartLifetime);
            if (part.num_of_parts < 1 || part.num_of_parts > 255 ||
                part.part_num < 1 || part.part_num > part.num_of_parts)
            {
                Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Invalid multipart segment number"));
                return false;
            }
            bool SameMessage(Common.MultipartMessage value) =>
                value.reference == part.reference && value.sender == part.sender &&
                value.recipient == part.recipient && value.body_format == part.body_format &&
                value.command == part.command;
            var matching = multipart_messages.Where(SameMessage).ToList();
            if (matching.Any(value => value.num_of_parts != part.num_of_parts))
            {
                Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Conflicting multipart segment count"));
                return false;
            }
            var duplicate = matching.FindIndex(value => value.part_num == part.part_num);
            if (duplicate >= 0)
            {
                if (matching[duplicate].short_message != part.short_message)
                    Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Conflicting multipart duplicate"));
                return false;
            }
            multipart_messages.Add(part);
            matching.Add(part);
            if (matching.Count != part.num_of_parts)
                return false;
            body = string.Concat(matching.OrderBy(value => value.part_num).Select(value => value.short_message));
            multipart_messages.RemoveAll(SameMessage);
            return true;
        }

        private void PduHandler(string pdu)
        {
            uint command = uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber);
            uint status = uint.Parse(pdu.Substring(16, 8), System.Globalization.NumberStyles.HexNumber);
            uint sequence = uint.Parse(pdu.Substring(24, 8), System.Globalization.NumberStyles.HexNumber);
            string name = Common.command_id.TryGetValue(command, out var knownName) ? knownName : "unknown";
            Notify(() => Events.LogChannelEvent(channel_name, "Receiving [" + name + "]", debug ? pdu : ""));
            switch ((Common.CommandId)command)
            {
                case Common.CommandId.bind_transceiver_resp:
                case Common.CommandId.bind_transmitter_resp:
                case Common.CommandId.bind_receiver_resp:
                    HandleBindResponse(command, sequence, status);
                    break;
                case Common.CommandId.generic_nack:
                    CompleteSubmission(sequence, status == 0 ? 1u : status, "");
                    if (pendingBindSequence == sequence)
                        Disconnect(true);
                    else
                        commands_queue.Remove(sequence);
                    break;
                case Common.CommandId.submit_sm:
                    var submit = new SubmitSm(pdu) { Is8bit = use8bit, UseGsm7 = UseGsm7 };
                    if (use8bit || UseGsm7)
                        submit.Decode();
                    DispatchInbound(submit, submit.Sender, submit.Recipient, submit.Body, submit.BodyFormat,
                        submit.RegisteredDelivery, submit.isMultipart ? submit.MultipartMessage : null, null,
                        ack => new SubmitSmResp { sequence_number = sequence, command_status = ack.CommandStatus, MessageID = ack.MessageId }.Encode(),
                        id =>
                    {
                        if (submit.isMultipart)
                        {
                            if (!TryAssemble(submit.MultipartMessage, out var assembled))
                                return;
                            submit.Body = assembled;
                        }
                        Notify(() => Events.LogChannelEvent(channel_name, "Message received from " + submit.Sender + " to " + submit.Recipient));
                        Notify(() => Events.LogNewMessageEvent(channel_name, id, submit.Sender, submit.Recipient,
                            submit.Body, submit.BodyFormat, submit.RegisteredDelivery));
                    });
                    break;
                case Common.CommandId.deliver_sm:
                    var deliver = new DeliverSm(pdu) { Is8bit = use8bit, UseGsm7 = UseGsm7 };
                    deliver.Decode();
                    bool deliverReceipt = (deliver.ESMClass & 0x3C) == 4;
                    var parsedReceipt = deliverReceipt ? DeliveryReceiptParser.Parse(channel_name, deliver.Body, deliver.tlv) : null;
                    DispatchInbound(deliver, deliver.Sender, deliver.Recipient, deliver.Body, deliver.BodyFormat,
                        deliver.RegisteredDelivery, deliver.isMultipart ? deliver.MultipartMessage : null, parsedReceipt,
                        ack => new DeliverSmResp { sequence_number = sequence, command_status = ack.CommandStatus, MessageID = ack.MessageId }.Encode(),
                        id =>
                    {
                        if (deliver.isMultipart)
                        {
                            if (!TryAssemble(deliver.MultipartMessage, out var assembled))
                                return;
                            deliver.Body = assembled;
                        }
                        if (deliverReceipt)
                            ReportDelivery(deliver.Body, deliver.tlv);
                        else
                        {
                            Notify(() => Events.LogChannelEvent(channel_name, "Message received from " + deliver.Sender + " to " + deliver.Recipient));
                            Notify(() => Events.LogNewMessageEvent(channel_name, id, deliver.Sender, deliver.Recipient,
                                deliver.Body, deliver.BodyFormat, deliver.RegisteredDelivery));
                        }
                    });
                    break;
                case Common.CommandId.data_sm:
                    var data = new DataSm(pdu) { UseGsm7 = UseGsm7 };
                    if (UseGsm7)
                        data.Decode();
                    bool dataReceipt = (data.ESMClass & 0x3C) == 4;
                    var parsedDataReceipt = dataReceipt ? DeliveryReceiptParser.Parse(channel_name, data.Body, data.tlv) : null;
                    DispatchInbound(data, data.Sender, data.Recipient, data.Body, data.BodyFormat,
                        data.RegisteredDelivery, data.isMultipart ? data.MultipartMessage : null, parsedDataReceipt,
                        ack => new DataSmResp { sequence_number = sequence, command_status = ack.CommandStatus, MessageID = ack.MessageId }.Encode(),
                        id =>
                    {
                        if (data.isMultipart)
                        {
                            if (!TryAssemble(data.MultipartMessage, out var assembled))
                                return;
                            data.Body = assembled;
                        }
                        if (dataReceipt)
                            ReportDelivery(data.Body, data.tlv);
                        else
                        {
                            Notify(() => Events.LogChannelEvent(channel_name, "Message received from " + data.Sender + " to " + data.Recipient));
                            Notify(() => Events.LogNewMessageEvent(channel_name, id, data.Sender, data.Recipient,
                                data.Body, data.BodyFormat, data.RegisteredDelivery));
                        }
                    });
                    break;
                case Common.CommandId.submit_sm_resp:
                    CompleteSubmission(sequence, status, new SubmitSmResp(pdu).MessageID);
                    break;
                case Common.CommandId.deliver_sm_resp:
                    CompleteSubmission(sequence, status, new DeliverSmResp(pdu).MessageID);
                    break;
                case Common.CommandId.data_sm_resp:
                    CompleteSubmission(sequence, status, new DataSmResp(pdu).MessageID);
                    break;
                case Common.CommandId.enquire_link:
                    var response = new EnquireLinkResp { sequence_number = sequence };
                    Notify(() => Events.LogChannelEvent(channel_name, "Sending [enquire_link_resp]"));
                    SendPDU(response.Encode());
                    break;
                case Common.CommandId.enquire_link_resp:
                    commands_queue.Remove(sequence);
                    break;
                case Common.CommandId.unbind:
                    SendPDU(new UnbindResp { sequence_number = sequence }.Encode());
                    Notify(() => Events.LogChannelEvent(channel_name, "Sending [unbind_resp]"));
                    Disconnect(true);
                    Notify(() => Events.LogChannelEvent(channel_name, "Disconnected"));
                    break;
                case Common.CommandId.unbind_resp:
                case Common.CommandId.alert_notification:
                case Common.CommandId.outbind:
                    break;
                default:
                    // Never acknowledge an unknown request as successful or NACK a response.
                    if ((command & 0x80000000) == 0)
                        SendPDU(new GenericNack { sequence_number = sequence, command_status = 3 }.Encode());
                    break;
            }
        }

        private void ReportDelivery(string body, SortedList<string, object> parameters)
        {
            var receipt = DeliveryReceiptParser.Parse(channel_name, body, parameters);
            if (receipt == null)
            {
                Notify(() => Events.LogEvent(LogEvent.Level.Error, channel_name + " - Delivery report has no valid ID/status"));
                return;
            }
            string legacyState = receipt.MessageState switch
            {
                1 => "ENROUT", 2 => "DELIVR", 3 => "EXPIRE", 4 => "DELETE",
                5 => "UNDELI", 6 => "ACCEPT", 7 => "UNKNOW", 8 => "REJECT", _ => ""
            };
            Notify(() => Events.LogChannelEvent(channel_name, "Message ref: " + receipt.ProviderMessageId + " " + legacyState));
            ReportReceipt(receipt);
        }

        public void SubmitDeliveryReport(string messageId, Common.MessageStatus status)
        {
            lock (SessionLock)
            {
                string messageStatus = status switch
                {
                    Common.MessageStatus.delivered_ACK_received or Common.MessageStatus.duplicate_ACK_detected => "DELIVRD",
                    Common.MessageStatus.ACK_expired or Common.MessageStatus.timeout => "EXPIRED",
                    Common.MessageStatus.queued or Common.MessageStatus.received_waiting_to_be_processed or
                        Common.MessageStatus.scheduled or Common.MessageStatus.received_routed => "ACCEPTD",
                    Common.MessageStatus.sent or Common.MessageStatus.submitted_waiting_for_ACK => "ENROUTE",
                    Common.MessageStatus.generic_error or Common.MessageStatus.locked_by_system or
                        Common.MessageStatus.NACK_received or Common.MessageStatus.rejected or Common.MessageStatus.out_of_balance => "REJECTD",
                    Common.MessageStatus.message_undeliverable or Common.MessageStatus.received_route_failure or
                        Common.MessageStatus.received_no_route_specified or Common.MessageStatus.no_channel_can_handle_message => "UNDELIV",
                    _ => "UNKNOWN"
                };
                var report = new DeliverSm(Events)
                {
                    sequence_number = NextSequenceNumber(), Sender = "", Recipient = "", BodyFormat = "ascii",
                    IsDeliveryReport = true,
                    Body = "id:" + messageId + " sub:001 dlvrd:" + (messageStatus == "DELIVRD" ? "001" : "000") +
                        " submit date:" + DateTime.UtcNow.ToString("yyMMddHHmm") +
                        " done date:" + DateTime.UtcNow.ToString("yyMMddHHmm") + " stat:" + messageStatus + " err:0 text:"
                };
                if (report.Body.Length > Common.SINGLE_ASCII_MESSAGE_LENGTH)
                    report.LargeMessageHandleMethod = 2;
                if (!string.IsNullOrEmpty(messageId) && messageId.Length <= 64 && messageId.All(value => value > 0 && value < 128))
                {
                    report.tlv["001E"] = Common.StringToHex(messageId) + "00";
                    report.tlv["0427"] = (messageStatus switch
                    {
                        "ENROUTE" => 1, "DELIVRD" => 2, "EXPIRED" => 3, "UNDELIV" => 5,
                        "ACCEPTD" => 6, "REJECTD" => 8, _ => 7
                    }).ToString("X2");
                }
                string pdu = report.EncodeM()[0];
                Notify(() => Events.LogChannelEvent(channel_name, "Sending [deliver_sm] report", debug ? pdu : ""));
                SendPDU(pdu);
            }
        }

        /// <summary>Stops this connection permanently and unregisters it. Remote disconnects retain registrations.</summary>
        public void Quit()
        {
            lock (SessionLock)
            {
                stopped = true;
                CancelReconnect();
                CancelConnectionAttempt();
                if (connected)
                {
                    var unbind = new Unbind { sequence_number = NextSequenceNumber() };
                    SendPDU(unbind.Encode());
                    Notify(() => Events.LogChannelEvent(channel_name, "Sending [unbind]"));
                }
                Disconnect(false);
                ClearDeliveryTracking();
                multipart_messages.Clear();
                if (is_server)
                {
                    lock (Gate.Servers)
                        if (Gate.Servers.TryGetValue(channel_name, out var server) && ReferenceEquals(server, this))
                            Gate.Servers.Remove(channel_name);
                }
                else
                {
                    lock (Gate.Clients)
                        if (Gate.Clients.TryGetValue(channel_name, out var client) && ReferenceEquals(client, this))
                            Gate.Clients.Remove(channel_name);
                }
            }
        }

        protected bool TryTrackSubmittedMessages(uint firstSequenceNumber, int partCount, submitted_message message)
        {
            lock (SessionLock)
            {
                for (int i = 0; i < partCount; i++)
                    if (submitted_messages.ContainsKey(firstSequenceNumber + (uint)i) || commands_queue.ContainsKey(firstSequenceNumber + (uint)i))
                    {
                        sequence_number = firstSequenceNumber + (uint)i + 1;
                        return false;
                    }
                message.submission = new Submission
                {
                    Remaining = partCount, FirstSequence = firstSequenceNumber,
                    MessageId = message.message_id, TrackDelivery = message.registered_delivery && registered_delivery == 1,
                    Parts = new DeliveryPart[partCount]
                };
                for (int i = 0; i < partCount; i++)
                    message.submission.Parts[i] = new DeliveryPart { Submission = message.submission, Number = i + 1 };
                for (int i = 0; i < partCount; i++)
                    submitted_messages.Add(firstSequenceNumber + (uint)i, message);
                sequence_number = firstSequenceNumber + (uint)partCount;
                if (sequence_number > 0x7FFFFFFF)
                    sequence_number = 1;
                return true;
            }
        }

        protected int SendMessage(int messageId, string sender, string recipient, bool deliveryReport,
            string commandName, Func<uint, byte, string[]> encode)
        {
            string[] parts;
            uint firstSequence;
            int generation;
            lock (SessionLock)
            {
                if (!connected)
                {
                    Notify(() => Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.received_route_failure));
                    return 1;
                }
                int second = (int)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond & int.MaxValue);
                if (second != last_second)
                {
                    last_second = second;
                    sent_during_last_second = 0;
                }
                if (max_threads > 0 && sent_during_last_second >= max_threads)
                {
                    Notify(() => Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.queued));
                    return 2;
                }
                multipart_sequence_number = multipart_sequence_number == 255 ? (byte)1 : (byte)(multipart_sequence_number + 1);
                firstSequence = sequence_number;
                parts = encode(firstSequence, multipart_sequence_number);
                uint available = FindAvailableSequenceRange(parts.Length);
                if (firstSequence != available)
                {
                    firstSequence = available;
                    parts = encode(firstSequence, multipart_sequence_number);
                }
                var message = new submitted_message
                {
                    message_id = messageId, submitted_time = DateTime.MaxValue, registered_delivery = deliveryReport
                };
                if (!TryTrackSubmittedMessages(firstSequence, parts.Length, message))
                {
                    Notify(() => Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.queued));
                    return 3;
                }
                sent_during_last_second++;
                generation = sessionGeneration;
                Notify(() => Events.LogChannelEvent(channel_name, "Sending from " + sender + " to " + recipient));
                Notify(() => Events.LogMessageChangeStatusEvent(messageId, registered_delivery == 1 && deliveryReport
                    ? Common.MessageStatus.submitted_waiting_for_ACK : Common.MessageStatus.received_routed));
            }
            for (int i = 0; i < parts.Length; i++)
            {
                lock (SessionLock)
                {
                    if (!connected || generation != sessionGeneration)
                        return 1;
                    uint sequence = firstSequence + (uint)i;
                    if (!submitted_messages.TryGetValue(sequence, out var message))
                        return 1; // This submission expired while waiting to send its next segment.
                    message.submitted_time = DateTime.UtcNow;
                    submitted_messages[sequence] = message;
                    string pdu = parts[i];
                    Notify(() => Events.LogChannelEvent(channel_name, "Sending [" + commandName + "]", debug ? pdu : ""));
                    int result = SendPDU(parts[i]);
                    if (result != 0)
                        return result;
                }
                if (i + 1 < parts.Length && multypart_delay > 0)
                    Thread.Sleep(multypart_delay);
            }
            return 0;
        }

        protected virtual void CancelConnectionAttempt() { }

        public virtual int SubmitSm(int message_id, string sender, string recipient, string body,
            string body_format = "ascii", bool delivery_report = true)
        {
            body_format = MessageBodyCodec.SelectFormat(body, body_format);
            return SendMessage(message_id, sender, recipient, delivery_report, "submit_sm", (sequence, reference) =>
                new SubmitSm
                {
                    sequence_number = sequence, MultipartSequenceNumber = reference,
                    Sender = sender, Recipient = recipient, Body = body, BodyFormat = body_format,
                    Is8bit = use8bit, ServiceType = system_type, SourceAddrTON = source_ton, SourceAddrNPI = source_npi,
                    DestAddrTON = destination_ton, DestAddrNPI = destination_npi, SplitBaseASCII = split_base_ascii,
                    RegisteredDelivery = registered_delivery == 1 && delivery_report ? 1 : 0,
                    LargeMessageHandleMethod = large_message
                }.EncodeM());
        }
    }
}
