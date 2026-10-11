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
    public abstract class Connection
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
        }

        protected TcpClient tcp_client;
        protected NetworkStream tcp_stream;

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

        private sealed class ReceiveState
        {
            internal NetworkStream Stream;
            internal byte[] Buffer = new byte[8192];
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
                        lock (SessionLock)
                        {
                            if (stopped || cancellation.IsCancellationRequested ||
                                !ReferenceEquals(reconnectCancellation, cancellation))
                                return;
                            reconnectCancellation = null;
                            Client client;
                            lock (Gate.Clients)
                                Gate.Clients.TryGetValue(channel_name, out client);
                            if (ReferenceEquals(client, this))
                                client.Connect();
                            else
                                Events.LogChannelEvent(channel_name, "Reconnect skipped: client is no longer registered");
                        }
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
            uint value = sequence_number;
            sequence_number = value >= 0x7FFFFFFF ? 1 : value + 1;
            return value;
        }

        protected void BeginSession(TcpClient client, bool bound)
        {
            tcp_client = client;
            tcp_stream = client.GetStream();
            tcp_stream.WriteTimeout = timeout;
            connected = bound;
            pendingBindSequence = null;
            _partial_pdu = "";
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
            Events.LogChannelEvent(channel_name, "Sending [" + name + "]");
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
                Events.LogChannelEvent(channel_name, "Sending [enquire_link]", debug ? pdu : "");
                SendPDU(pdu);
            }
        }

        internal void Receive()
        {
            lock (SessionLock)
            {
                if (tcp_stream == null || stopped)
                    return;
                var state = new ReceiveState { Stream = tcp_stream };
                try
                {
                    state.Stream.BeginRead(state.Buffer, 0, state.Buffer.Length, receiveCallback, state);
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException)
                {
                    Events.LogChannelEvent(channel_name, "Receive failed: " + ex.Message);
                    Disconnect(true);
                }
            }
        }

        protected void receiveCallback(IAsyncResult ar)
        {
            var state = (ReceiveState)ar.AsyncState;
            try
            {
                int count = state.Stream.EndRead(ar);
                lock (SessionLock)
                {
                    // A callback from a closed socket must never touch its replacement session.
                    if (!ReferenceEquals(tcp_stream, state.Stream))
                        return;
                    if (count == 0)
                    {
                        Disconnect(true);
                        return;
                    }
                    MessageHandler(Common.ConvertByteArrayToHexString(state.Buffer, count));
                    if (ReferenceEquals(tcp_stream, state.Stream))
                        Receive();
                }
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException || ex is SocketException)
            {
                lock (SessionLock)
                {
                    if (!ReferenceEquals(tcp_stream, state.Stream))
                        return;
                    Events.LogChannelEvent(channel_name, "Receive failed: " + ex.Message);
                    Disconnect(true);
                }
            }
        }

        void timeout_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            lock (SessionLock)
            {
                var now = DateTime.UtcNow;
                multipart_messages.RemoveAll(part => now - part.submitted_dt >= MultipartLifetime);
                if (stopped || tcp_stream == null)
                    return;
                bool expired = commands_queue.Values.Any(time => (now - time).TotalMilliseconds >= timeout) ||
                    submitted_messages.Values.Any(message => (now - message.submitted_time).TotalMilliseconds >= timeout);
                if (expired)
                {
                    Events.LogChannelEvent(channel_name, "Command timeout");
                    Disconnect(true);
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
                var stream = tcp_stream;
                var client = tcp_client;
                tcp_stream = null;
                tcp_client = null;
                sessionGeneration++;
                stream?.Dispose();
                client?.Dispose();
                commands_queue.Clear();
                _partial_pdu = "";
                multipart_messages.Clear();
                var pending = submitted_messages.Values.Select(message => message.message_id).Distinct().ToArray();
                submitted_messages.Clear();
                if (reconnect)
                    ScheduleReconnect();
                foreach (int id in pending)
                    Events.LogMessageChangeStatusEvent(id, Common.MessageStatus.timeout);
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
                    tcp_stream.WriteTimeout = timeout;
                    tcp_stream.Write(bytes, 0, bytes.Length);
                    return 0;
                }
                catch (Exception ex) when (ex is System.IO.IOException || ex is ObjectDisposedException || ex is SocketException)
                {
                    Events.LogChannelEvent(channel_name, "Send failed: " + ex.Message);
                    Disconnect(true);
                    return 502;
                }
            }
        }

        public void MessageHandler(object mess)
        {
            lock (SessionLock)
            {
                var data = new List<string>();
                _partial_pdu += mess?.ToString();
                while (_partial_pdu.Length >= 8)
                {
                    if (!uint.TryParse(_partial_pdu.Substring(0, 8), System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out uint length) ||
                        length < 16 || length > Common.MAX_PDU_LENGTH)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + " - Invalid PDU length");
                        _partial_pdu = "";
                        break;
                    }
                    if (_partial_pdu.Length < length * 2)
                        break;
                    data.Add(_partial_pdu.Substring(0, (int)length * 2));
                    _partial_pdu = _partial_pdu.Substring((int)length * 2);
                }

                int generation = sessionGeneration;
                foreach (string pdu in data)
                {
                    if (generation != sessionGeneration)
                        break;
                    try { PduHandler(pdu); }
                    catch (Exception ex)
                    {
                        // A malformed PDU or application callback must not stop subsequent reads.
                        Events.LogEvent(LogEvent.Level.Error, channel_name + " - PDU handling failed: " + ex.Message);
                    }
                }
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
                Events.LogChannelEvent(channel_name, "Bound successfully");
            }
            else
            {
                Events.LogChannelEvent(channel_name, "Bind rejected: " + status);
                Disconnect(true);
            }
        }

        private void CompleteSubmission(uint sequence, uint status, string messageId)
        {
            if (!submitted_messages.TryGetValue(sequence, out var message))
                return;
            submitted_messages.Remove(sequence);
            Events.LogMessageChangeStatusEvent(message.message_id,
                status == 0 ? Common.MessageStatus.sent : Common.MessageStatus.rejected, messageId ?? "");
        }

        private bool TryAssemble(Common.MultipartMessage part, out string body)
        {
            body = null;
            var now = DateTime.UtcNow;
            multipart_messages.RemoveAll(value => now - value.submitted_dt >= MultipartLifetime);
            if (part.num_of_parts < 1 || part.num_of_parts > 255 ||
                part.part_num < 1 || part.part_num > part.num_of_parts)
            {
                Events.LogEvent(LogEvent.Level.Error, channel_name + " - Invalid multipart segment number");
                return false;
            }
            bool SameMessage(Common.MultipartMessage value) =>
                value.reference == part.reference && value.sender == part.sender &&
                value.recipient == part.recipient && value.body_format == part.body_format &&
                value.command == part.command;
            var matching = multipart_messages.Where(SameMessage).ToList();
            if (matching.Any(value => value.num_of_parts != part.num_of_parts))
            {
                Events.LogEvent(LogEvent.Level.Error, channel_name + " - Conflicting multipart segment count");
                return false;
            }
            var duplicate = matching.FindIndex(value => value.part_num == part.part_num);
            if (duplicate >= 0)
            {
                if (matching[duplicate].short_message != part.short_message)
                    Events.LogEvent(LogEvent.Level.Error, channel_name + " - Conflicting multipart duplicate");
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
            Events.LogChannelEvent(channel_name, "Receiving [" + name + "]", debug ? pdu : "");
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
                    var submit = new SubmitSm(pdu) { Is8bit = use8bit };
                    if (use8bit)
                        submit.Decode();
                    var submitResponse = new SubmitSmResp { sequence_number = sequence, MessageID = Guid.NewGuid().ToString("n") };
                    Events.LogChannelEvent(channel_name, "Sending [submit_sm_resp]");
                    SendPDU(submitResponse.Encode());
                    if (submit.isMultipart)
                    {
                        if (!TryAssemble(submit.MultipartMessage, out var assembledSubmit))
                            break;
                        submit.Body = assembledSubmit;
                    }
                    Events.LogChannelEvent(channel_name, "Message received from " + submit.Sender + " to " + submit.Recipient);
                    Events.LogNewMessageEvent(channel_name, submitResponse.MessageID, submit.Sender, submit.Recipient,
                        submit.Body, submit.BodyFormat, submit.RegisteredDelivery);
                    break;
                case Common.CommandId.deliver_sm:
                    var deliver = new DeliverSm(pdu) { Is8bit = use8bit };
                    deliver.Decode();
                    var deliverResponse = new DeliverSmResp { sequence_number = sequence, MessageID = Guid.NewGuid().ToString("n") };
                    Events.LogChannelEvent(channel_name, "Sending [deliver_sm_resp]");
                    SendPDU(deliverResponse.Encode());
                    if (deliver.isMultipart)
                    {
                        if (!TryAssemble(deliver.MultipartMessage, out var assembledDeliver))
                            break;
                        deliver.Body = assembledDeliver;
                    }
                    if ((deliver.ESMClass & 0x3C) == 4)
                    {
                        ReportDelivery(deliver);
                        break;
                    }
                    Events.LogChannelEvent(channel_name, "Message received from " + deliver.Sender + " to " + deliver.Recipient);
                    Events.LogNewMessageEvent(channel_name, deliverResponse.MessageID, deliver.Sender, deliver.Recipient,
                        deliver.Body, deliver.BodyFormat, deliver.RegisteredDelivery);
                    break;
                case Common.CommandId.data_sm:
                    var data = new DataSm(pdu);
                    var dataResponse = new DataSmResp { sequence_number = sequence, MessageID = Guid.NewGuid().ToString("n") };
                    Events.LogChannelEvent(channel_name, "Sending [data_sm_resp]");
                    SendPDU(dataResponse.Encode());
                    Events.LogChannelEvent(channel_name, "Message received from " + data.Sender + " to " + data.Recipient);
                    Events.LogNewMessageEvent(channel_name, dataResponse.MessageID, data.Sender, data.Recipient,
                        data.Body, data.BodyFormat, data.RegisteredDelivery);
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
                    Events.LogChannelEvent(channel_name, "Sending [enquire_link_resp]");
                    SendPDU(response.Encode());
                    break;
                case Common.CommandId.enquire_link_resp:
                    commands_queue.Remove(sequence);
                    break;
                case Common.CommandId.unbind:
                    SendPDU(new UnbindResp { sequence_number = sequence }.Encode());
                    Events.LogChannelEvent(channel_name, "Sending [unbind_resp]");
                    Disconnect(true);
                    Events.LogChannelEvent(channel_name, "Disconnected");
                    break;
                case Common.CommandId.unbind_resp:
                    break;
                default:
                    // Never acknowledge an unknown request as successful or NACK a response.
                    if ((command & 0x80000000) == 0)
                        SendPDU(new GenericNack { sequence_number = sequence, command_status = 3 }.Encode());
                    break;
            }
        }

        private void ReportDelivery(DeliverSm deliver)
        {
            // Reparse after multipart assembly so ID and status can be in different segments.
            deliver.ParseDeliveryReport();
            Common.MessageStatus? status = deliver.DeliveryStatus switch
            {
                "DELIVR" => Common.MessageStatus.delivered_ACK_received,
                "EXPIRE" => Common.MessageStatus.ACK_expired,
                "UNKNOW" => Common.MessageStatus.unknown_recipient,
                "ACCEPT" => Common.MessageStatus.received_routed,
                "REJECT" => Common.MessageStatus.rejected,
                "UNDELI" or "DELETE" => Common.MessageStatus.message_undeliverable,
                "ENROUT" => Common.MessageStatus.submitted_waiting_for_ACK,
                _ => null
            };
            if (status == null || string.IsNullOrWhiteSpace(deliver.Reference))
            {
                Events.LogEvent(LogEvent.Level.Error, channel_name + " - Delivery report has no valid ID/status");
                return;
            }
            Events.LogChannelEvent(channel_name, "Message ref: " + deliver.Reference + " " + deliver.DeliveryStatus);
            Events.LogMessageDeliverReportEvent(deliver.Reference, status.Value);
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
                string pdu = report.EncodeM()[0];
                Events.LogChannelEvent(channel_name, "Sending [deliver_sm] report", debug ? pdu : "");
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
                if (connected)
                {
                    var unbind = new Unbind { sequence_number = NextSequenceNumber() };
                    SendPDU(unbind.Encode());
                    Events.LogChannelEvent(channel_name, "Sending [unbind]");
                }
                Disconnect(false);
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
                    if (submitted_messages.ContainsKey(firstSequenceNumber + (uint)i))
                        return false;
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
                    Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.received_route_failure);
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
                    Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.queued);
                    return 2;
                }
                multipart_sequence_number = multipart_sequence_number == 255 ? (byte)1 : (byte)(multipart_sequence_number + 1);
                firstSequence = sequence_number;
                parts = encode(firstSequence, multipart_sequence_number);
                if ((ulong)firstSequence + (uint)parts.Length - 1 > 0x7FFFFFFF)
                {
                    firstSequence = 1;
                    parts = encode(firstSequence, multipart_sequence_number);
                }
                var message = new submitted_message
                {
                    message_id = messageId, submitted_time = DateTime.MaxValue, registered_delivery = deliveryReport
                };
                if (!TryTrackSubmittedMessages(firstSequence, parts.Length, message))
                {
                    Events.LogMessageChangeStatusEvent(messageId, Common.MessageStatus.queued);
                    return 3;
                }
                sent_during_last_second++;
                generation = sessionGeneration;
                Events.LogChannelEvent(channel_name, "Sending from " + sender + " to " + recipient);
            }
            for (int i = 0; i < parts.Length; i++)
            {
                lock (SessionLock)
                {
                    if (!connected || generation != sessionGeneration)
                        return 1;
                    uint sequence = firstSequence + (uint)i;
                    var message = submitted_messages[sequence];
                    message.submitted_time = DateTime.UtcNow;
                    submitted_messages[sequence] = message;
                    Events.LogMessageChangeStatusEvent(messageId, registered_delivery == 1 && deliveryReport
                        ? Common.MessageStatus.submitted_waiting_for_ACK : Common.MessageStatus.received_routed);
                    Events.LogChannelEvent(channel_name, "Sending [" + commandName + "]", debug ? parts[i] : "");
                    int result = SendPDU(parts[i]);
                    if (result != 0)
                        return result;
                }
                if (i + 1 < parts.Length && multypart_delay > 0)
                    Thread.Sleep(multypart_delay);
            }
            return 0;
        }

        public virtual int SubmitSm(int message_id, string sender, string recipient, string body,
            string body_format = "ascii", bool delivery_report = true)
        {
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
