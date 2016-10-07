using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Smpp.Exceptions;
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
        protected AsyncCallback _callbackSend;
        protected AsyncCallback _callbackReceive;

        /// <summary>
        /// buffer which receives incoming PDU
        /// </summary>
        byte[] _buffer;
        string _partial_pdu = "";

        /// <summary>
        /// if stream connected and binded
        /// if not connected try to connect and bind
        /// </summary>
        public bool connected;

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

        /// <summary>
        /// Interval in milliseconds to send enquire link, 1 minute by default
        /// </summary>
        public int keep_alive = 60000;

        /// <summary>
        /// Timeout in milliseconds if remote host does not answer, 10 seconds by default
        /// </summary>
        public int timeout = 10000;

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

        /// <summary>
        /// The time of last pdu received
        /// will be compared with last enquire_link time 
        /// so if client will be overloaded and enquire_link will be
        /// delayed, the connection will not be dropped
        /// </summary>
        DateTime last_pdu_time;

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
            Events = events;

            _callbackSend = new AsyncCallback(sendCallback);
            _callbackReceive = new AsyncCallback(receiveCallback);

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
            last_pdu_time = DateTime.Now.AddHours(-1);
        }

        void enquire_link_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (!Gate.Servers.ContainsKey(channel_name) &&
                !Gate.Clients.ContainsKey(channel_name))
            {
                Events.LogChannelEvent(channel_name, "Connection is broken");

                Task.Run(() =>
                {
                    enquire_link_timer.Stop();
                    timeout_timer.Stop();
                    Gate.Clients[channel_name].Connect();
                });

                return;
            }

            if (connected)
            {
                SendEnquireLink();
            }
        }

        internal void Receive()
        {
            try
            {
                // If Socket can receive
                if (tcp_stream != null && tcp_stream.CanRead)
                {
                    _buffer = new byte[Common.MAX_RECEIVE_LENGTH];
                    tcp_stream.BeginRead(_buffer, 0, _buffer.Length, receiveCallback, null);
                }
            }
            catch
            {
                ;// Communication issues, bad stream, ignore
            }
        }

        protected void receiveCallback(IAsyncResult ar)
        {
            var myCompleteMessage = new StringBuilder();
            int numberOfBytesRead;

            if (tcp_stream != null && tcp_stream.CanRead)
            {
                try
                {
                    numberOfBytesRead = tcp_stream.EndRead(ar);
                    myCompleteMessage.Append(_partial_pdu);
                    _partial_pdu = "";
                    myCompleteMessage.Append(Common.ConvertByteArrayToHexString(_buffer, numberOfBytesRead));

                    if (numberOfBytesRead > 0)
                    {
                        new Thread(new ParameterizedThreadStart(this.MessageHandler)).Start(myCompleteMessage.ToString());
                    }
                }
                catch
                {
                    // socket errors or just trash in network, just ignore
                }
                finally
                {
                    Receive();
                }
            }
        }

        protected void sendCallback(IAsyncResult ar)
        {
            try
            {
                if (tcp_stream.CanWrite)
                {
                    tcp_stream.EndWrite(ar);
                    connected = true;
                }
            }
            catch
            {
                ; // Communication issues, bad stream, ignore
            }
        }

        void timeout_timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            TimeSpan span;

            timeout_timer.Stop();

            lock (commands_queue)
            {
                foreach (uint key in commands_queue.Keys)
                {
                    span = DateTime.Now.Subtract(commands_queue[key]);
                    if (span.TotalMilliseconds > timeout &&
                        ((DateTime.Now.Subtract(last_pdu_time)).TotalMilliseconds > 1000))
                    {
                        connected = false;
                        Events.LogChannelEvent(channel_name, "Command timeout");
                        Events.LogChannelEvent(channel_name, "Disconnecting.... ");

                        timeout_timer.Stop();
                        enquire_link_timer.Stop();

                        tcp_stream.Close();
                        tcp_client.Close();
                        commands_queue.Clear();

                        // mark all messages which are waiting for submit_sm_resp
                        // as timed out
                        lock (submitted_messages)
                        {
                            foreach (submitted_message message in submitted_messages.Values)
                            {
                                Events.LogMessageChangeStatusEvent(message.message_id, Common.MessageStatus.timeout);
                            }
                        }
                        submitted_messages.Clear();

                        multipart_messages.Clear();

                        if (!is_server)
                        {
                            // try to reconnect
                            Thread.Sleep(reconnectDelay);
                            Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                        }
                        else
                        {
                            Gate.Servers.Remove(channel_name);
                        }

                        return;
                    }
                }
            }
        }

        public int SendPDU(string pdu)
        {
            byte[] msg;

            msg = Common.ConvertHexStringToByteArray(pdu);

            sequence_number++;
            try
            {
                tcp_stream.BeginWrite(msg, 0, msg.Length, sendCallback, null);
            }
            catch
            {
                if (!connected)
                {
                    return 1;
                }

                connected = false;

                Events.LogChannelEvent(channel_name, "Command sending time out");
                Events.LogChannelEvent(channel_name, "Disconnecting.... ");

                timeout_timer.Stop();
                enquire_link_timer.Stop();

                tcp_stream.Close();
                tcp_client.Close();
                commands_queue.Clear();

                // mark all messages which are waiting for submit_sm_resp
                // as timed out
                lock (submitted_messages)
                {
                    foreach (submitted_message message in submitted_messages.Values)
                    {
                        Events.LogMessageChangeStatusEvent(message.message_id, Common.MessageStatus.timeout);
                    }
                }
                submitted_messages.Clear();

                if (!is_server)
                {
                    Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                }
                else
                {
                    Gate.Servers.Remove(channel_name);
                }
                return 502;
            }

            return 0;
        }

        public void MessageHandler(object mess)
        {
            var data = new List<string>();
            int PDULength = 0;
            last_pdu_time = DateTime.Now;

            try
            {
                _partial_pdu += mess.ToString();
                PDULength = int.Parse(_partial_pdu.Substring(0, 8), System.Globalization.NumberStyles.HexNumber);
                if (PDULength > 1500)
                {
                    _partial_pdu = "";
                    return;
                }
                while (PDULength != 0)
                {
                    data.Add(_partial_pdu.Substring(0, PDULength * 2));
                    _partial_pdu = _partial_pdu.Substring(PDULength * 2);

                    PDULength = _partial_pdu != "" ? int.Parse(_partial_pdu.Substring(0, 8), System.Globalization.NumberStyles.HexNumber) : 0;
                }
            }
            catch (CommandLengthException)
            {
                Events.LogEvent(LogEvent.Level.Error, "Command length exception");
            }
            catch (Exception e1)
            {
                Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1 + ":" + PDULength);
                _partial_pdu = "";
                return;
            }

            foreach (string pdu in data)
            {
                if (string.IsNullOrEmpty(pdu) || pdu.Length < 8) continue;
                try
                {
                    PduHandler(pdu);
                }
                catch (Exception e1)
                {
                    Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1 + ":" + pdu);
                }
            }
        }

        /// <summary>
        /// Gets PDU from stream, parses 
        /// </summary>
        /// <param name="pdu">PDU to handle</param>
        private void PduHandler(string pdu)
        {
            string response = string.Empty;

            Events.LogChannelEvent(channel_name, "Receiving [" + Common.command_id.Values[Common.command_id.IndexOfKey(uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber))] + "]", debug ? pdu : "");

            switch (Common.command_id.Values[Common.command_id.IndexOfKey(uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber))])
            {
                case "generic_nack":
                    // they do not understand my command 
                    // may be a bug in my pdu
                    break;
                case "bind_transceiver_resp":
                    var bind_transceiver = new BindTransceiverResp(pdu);
                    commands_queue.Remove(bind_transceiver.sequence_number);
                    if (bind_transceiver.command_status != 0)
                    {
                        Events.LogChannelEvent(channel_name, "Error: " + Common.command_status[bind_transceiver.command_status].description);
                        if (!is_server)
                        {
                            // try to reconnect
                            Thread.Sleep(reconnectDelay);
                            Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                        }
                    }
                    break;
                case "bind_transmitter_resp":
                    var bind_transmitter = new BindTransmitterResp(pdu);
                    commands_queue.Remove(bind_transmitter.sequence_number);
                    if (bind_transmitter.command_status != 0)
                    {
                        Events.LogChannelEvent(channel_name, "Error: " + Common.command_id[bind_transmitter.command_status]);

                        if (!is_server)
                        {
                            Thread.Sleep(reconnectDelay);
                            Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                        }
                    }

                    break;
                case "bind_receiver_resp":
                    var bind_receiver = new BindReceiverResp(pdu);
                    commands_queue.Remove(bind_receiver.sequence_number);
                    if (bind_receiver.command_status != 0)
                    {
                        Events.LogChannelEvent(channel_name, "Error: " + Common.command_id[bind_receiver.command_status]);
                        Quit();

                        if (!is_server)
                        {
                            Thread.Sleep(reconnectDelay);
                            Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                        }
                    }

                    break;
                case "submit_sm":
                    var submit_sm_s = new SubmitSm(pdu);
                    var submit_sm_resp_s = new SubmitSmResp();

                    submit_sm_resp_s.MessageID = Guid.NewGuid().ToString("n");
                    submit_sm_resp_s.sequence_number = submit_sm_s.sequence_number;
                    response = submit_sm_resp_s.Encode();

                    Events.LogChannelEvent(channel_name, "Message received from " + submit_sm_s.Sender + " to " + submit_sm_s.Recipient);
                    Events.LogChannelEvent(channel_name, "Sending [submit_sm_resp]");
                    SendPDU(response);

                    if (submit_sm_s.isMultipart)
                    {
                        int parts = 0;
                        lock (multipart_messages)
                        {
                            multipart_messages.Add(submit_sm_s.MultipartMessage);

                            foreach (Common.MultipartMessage mm in multipart_messages)
                            {
                                if (mm.reference == submit_sm_s.MultipartMessage.reference)
                                {
                                    parts++;
                                }
                            }
                        }
                        if (parts == submit_sm_s.MultipartMessage.num_of_parts)
                        {
                            var for_remove = new List<Common.MultipartMessage>();
                            var short_message = new StringBuilder();
                            var str = new string[parts];
                            lock (multipart_messages)
                            {
                                foreach (Common.MultipartMessage mm in multipart_messages)
                                {
                                    if (mm.reference == submit_sm_s.MultipartMessage.reference)
                                    {
                                        for_remove.Add(mm);
                                        try
                                        {
                                            str[mm.part_num - 1] = mm.short_message;
                                        }
                                        catch (Exception e1)
                                        {
                                            Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1 + "  " + pdu);
                                        }
                                    }
                                }

                                foreach (Common.MultipartMessage k in for_remove)
                                {
                                    multipart_messages.Remove(k);
                                }
                            }
                            // can be required
                            // short_message.Append("(" + submit_sm_s.MultipartMessage.reference.ToString() + ") ");
                            try
                            {
                                for (int k = 0; k < parts; k++)
                                {
                                    short_message.Append(str[k]);
                                }
                                submit_sm_s.Body = short_message.ToString();
                            }
                            catch (Exception e1)
                            {
                                Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1 + "  " + pdu);
                            }

                            Events.LogNewMessageEvent(channel_name, submit_sm_resp_s.MessageID, submit_sm_s.Sender, submit_sm_s.Recipient, submit_sm_s.Body, submit_sm_s.BodyFormat, submit_sm_s.RegisteredDelivery);
                        }
                    }
                    else
                    {
                        Events.LogNewMessageEvent(channel_name, submit_sm_resp_s.MessageID, submit_sm_s.Sender, submit_sm_s.Recipient, submit_sm_s.Body, submit_sm_s.BodyFormat, submit_sm_s.RegisteredDelivery);
                    }
                    break;
                case "submit_sm_resp":
                    try
                    {
                        var submit_sm_resp = new SubmitSmResp(pdu);
                        if (submitted_messages.ContainsKey(submit_sm_resp.sequence_number))
                        {
                            var submitSmRespMessageId = "";
                            if (!string.IsNullOrEmpty(submit_sm_resp.MessageID))
                            {
                                submitSmRespMessageId = submit_sm_resp.MessageID;
                            }

                            if (submit_sm_resp.command_status == 0)
                            {
                                Events.LogMessageChangeStatusEvent(
                                    submitted_messages[submit_sm_resp.sequence_number].message_id, Common.MessageStatus.sent, submitSmRespMessageId);
                            }
                            else
                            {
                                if (Common.command_status.ContainsKey(submit_sm_resp.command_status))
                                {
                                    Events.LogMessageChangeStatusEvent(
                                        submitted_messages[submit_sm_resp.sequence_number].message_id,
                                        Common.MessageStatus.rejected, submitSmRespMessageId);
                                }
                                else
                                {
                                    Events.LogMessageChangeStatusEvent(
                                        submitted_messages[submit_sm_resp.sequence_number].message_id,
                                        Common.MessageStatus.sent);
                                }
                            }
                            submitted_messages.Remove(submit_sm_resp.sequence_number);
                        }

                        if (submit_sm_resp.command_status != 0)
                        {
                            Events.LogChannelEvent(channel_name, "Error sending: " + submit_sm_resp.command_status);
                        }
                        else
                        {
                            Events.LogChannelEvent(channel_name, "Message reference: " + submit_sm_resp.MessageID);
                        }
                    }
                    catch (Exception e1)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1.ToString());
                    }
                    break;
                case "enquire_link":
                    var enquire_link = new EnquireLink(pdu);
                    var enquire_link_resp = new EnquireLinkResp();

                    enquire_link_resp.sequence_number = enquire_link.sequence_number;
                    response = enquire_link_resp.Encode();
                    SendPDU(response);
                    Events.LogChannelEvent(channel_name, "Sending [enquire_link_resp]", debug ? response : "");
                    break;
                case "enquire_link_resp":
                    var enquire_link_resp_o = new EnquireLinkResp(pdu);
                    commands_queue.Remove(enquire_link_resp_o.sequence_number);
                    break;
                case "deliver_sm":
                    DeliverSm deliver_sm;
                    try
                    {
                        deliver_sm = new DeliverSm(pdu);
                        deliver_sm.Is8bit = use8bit;
                        deliver_sm.Decode();
                    }
                    catch (CommandLengthException)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + ". Command length error.");
                        break;
                    }
                    catch (Exception e1)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + ". Error: " + e1.Message);
                        break;
                    }

                    // Send Resp
                    var deliver_sm_resp = new DeliverSmResp();
                    deliver_sm_resp.MessageID = Guid.NewGuid().ToString("n");
                    deliver_sm_resp.sequence_number = deliver_sm.sequence_number;
                    response = deliver_sm_resp.Encode();

                    Events.LogChannelEvent(channel_name, "Sending [deliver_sm_resp]", debug ? response : "");
                    SendPDU(response);

                    // Multipart message
                    if (deliver_sm.isMultipart)
                    {
                        int parts = 0;
                        lock (multipart_messages)
                        {
                            multipart_messages.Add(deliver_sm.MultipartMessage);


                            foreach (Common.MultipartMessage mm in multipart_messages)
                            {
                                if (mm.reference == deliver_sm.MultipartMessage.reference)
                                {
                                    parts++;
                                }
                            }
                        }
                        if (parts == deliver_sm.MultipartMessage.num_of_parts)
                        {
                            List<Common.MultipartMessage> for_remove = new List<Common.MultipartMessage>();
                            StringBuilder short_message = new StringBuilder();
                            string[] str = new string[parts];

                            lock (multipart_messages)
                            {
                                foreach (Common.MultipartMessage mm in multipart_messages)
                                {
                                    if (mm.reference == deliver_sm.MultipartMessage.reference)
                                    {
                                        for_remove.Add(mm);
                                        try
                                        {
                                            str[mm.part_num - 1] = mm.short_message;
                                        }
                                        catch (Exception e1)
                                        {
                                            Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1.ToString() + "  " + pdu);
                                        }
                                    }
                                }

                                foreach (Common.MultipartMessage k in for_remove)
                                {
                                    multipart_messages.Remove(k);
                                }
                            }

                            try
                            {
                                for (int k = 0; k < parts; k++)
                                {
                                    short_message.Append(str[k]);
                                }
                                deliver_sm.Body = short_message.ToString();
                            }
                            catch (Exception e1)
                            {
                                Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1.ToString() + "  " + pdu);
                            }
                        }
                        else
                        {
                            // Multipart message is not built yet, wait for the rest 
                            break;
                        }

                    }

                    // Delivery report
                    if (deliver_sm.ESMClass == 4)
                    {
                        Events.LogChannelEvent(channel_name, "Message ref: " + deliver_sm.Reference + " " + deliver_sm.DeliveryStatus, debug ? pdu : "");
                        if (deliver_sm.DeliveryStatus.ToUpper() == "DELIVR")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.delivered_ACK_received);
                        }
                        if (deliver_sm.DeliveryStatus.ToUpper() == "EXPIRE")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.ACK_expired);
                        }
                        if (deliver_sm.DeliveryStatus.ToUpper() == "UNKNOW")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.unknown_recipient);
                        }
                        if (deliver_sm.DeliveryStatus.ToUpper() == "ACCEPT")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.received_routed);
                        }
                        if (deliver_sm.DeliveryStatus.ToUpper() == "REJECT")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.rejected);
                        }
                        if (deliver_sm.DeliveryStatus.ToUpper() == "UNDELI")
                        {
                            Events.LogMessageDeliverReportEvent(deliver_sm.Reference, Common.MessageStatus.message_undeliverable);
                        }

                        break;
                    }

                    // Log message
                    Events.LogChannelEvent(channel_name, "Message received from " + deliver_sm.Sender + " to " + deliver_sm.Recipient, debug ? pdu : "");
                    Events.LogNewMessageEvent(channel_name, deliver_sm_resp.MessageID, deliver_sm.Sender, deliver_sm.Recipient, deliver_sm.Body, deliver_sm.BodyFormat, deliver_sm.RegisteredDelivery);

                    break;
                case "deliver_sm_resp":
                    try
                    {
                        var deliver_sm_resp2 = new DeliverSmResp(pdu);
                        if (submitted_messages.ContainsKey(deliver_sm_resp2.sequence_number))
                        {
                            var deliverSmRespMessageId = "";
                            if (!string.IsNullOrEmpty(deliver_sm_resp2.MessageID))
                            {
                                deliverSmRespMessageId = deliver_sm_resp2.MessageID;
                            }

                            if (deliver_sm_resp2.command_status == 0)
                            {
                                if (registered_delivery != 1 && submitted_messages[deliver_sm_resp2.sequence_number].registered_delivery)
                                {
                                    Events.LogMessageChangeStatusEvent(submitted_messages[deliver_sm_resp2.sequence_number].message_id, Common.MessageStatus.sent, deliverSmRespMessageId);
                                }
                            }
                            else
                            {
                                if (Common.command_status.ContainsKey(deliver_sm_resp2.command_status))
                                {
                                    Events.LogMessageChangeStatusEvent(
                                        submitted_messages[deliver_sm_resp2.sequence_number].message_id,
                                        Common.MessageStatus.rejected, deliverSmRespMessageId);
                                }
                                else
                                {
                                    Events.LogMessageChangeStatusEvent(
                                        submitted_messages[deliver_sm_resp2.sequence_number].message_id,
                                        Common.MessageStatus.sent, deliverSmRespMessageId);
                                }
                            }
                            submitted_messages.Remove(deliver_sm_resp2.sequence_number);
                        }

                        if (deliver_sm_resp2.command_status != 0)
                        {
                            Events.LogChannelEvent(channel_name, "Error sending: " + deliver_sm_resp2.command_status);
                        }
                        else
                        {
                            Events.LogChannelEvent(channel_name, "Message reference: " + deliver_sm_resp2.MessageID);
                        }
                    }
                    catch (Exception e1)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + " - " + e1);
                    }
                    break;
                case "data_sm":
                    var data_sm_s = new DataSm(pdu);
                    var data_sm_resp_s = new DataSmResp();

                    data_sm_resp_s.MessageID = Guid.NewGuid().ToString("n");
                    data_sm_resp_s.sequence_number = data_sm_s.sequence_number;
                    response = data_sm_resp_s.Encode();

                    Events.LogChannelEvent(channel_name, "Message received from " + data_sm_s.Sender + " to " + data_sm_s.Recipient, debug ? pdu : "");
                    Events.LogChannelEvent(channel_name, "Sending [data_sm_resp]");
                    SendPDU(response);

                    Events.LogNewMessageEvent(channel_name, data_sm_resp_s.MessageID, data_sm_s.Sender, data_sm_s.Recipient, data_sm_s.Body, data_sm_s.BodyFormat, data_sm_s.RegisteredDelivery);

                    break;
                case "data_sm_resp":
                    try
                    {
                        var data_sm_resp = new DataSmResp(pdu);
                        if (submitted_messages.ContainsKey(data_sm_resp.sequence_number))
                        {
                            var dataSmRespMessageId = "";
                            if (!string.IsNullOrEmpty(data_sm_resp.MessageID))
                            {
                                dataSmRespMessageId = data_sm_resp.MessageID;
                            }

                            if (data_sm_resp.command_status == 0)
                            {
                                if (registered_delivery != 1 && submitted_messages[data_sm_resp.sequence_number].registered_delivery)
                                {
                                    Events.LogMessageChangeStatusEvent(submitted_messages[data_sm_resp.sequence_number].message_id, Common.MessageStatus.sent, dataSmRespMessageId);
                                }
                            }
                            else
                            {
                                if (Common.command_status.ContainsKey(data_sm_resp.command_status))
                                {
                                    Events.LogMessageChangeStatusEvent(submitted_messages[data_sm_resp.sequence_number].message_id, Common.MessageStatus.rejected, dataSmRespMessageId);
                                }
                                else
                                {
                                    Events.LogMessageChangeStatusEvent(submitted_messages[data_sm_resp.sequence_number].message_id, Common.MessageStatus.sent, dataSmRespMessageId);
                                }
                            }
                            submitted_messages.Remove(data_sm_resp.sequence_number);
                        }

                        if (data_sm_resp.command_status != 0)
                        {
                            Events.LogChannelEvent(channel_name, "Error sending: " + data_sm_resp.command_status);
                        }
                        else
                        {
                            Events.LogChannelEvent(channel_name, "Message reference: " + data_sm_resp.MessageID);
                        }
                    }
                    catch (Exception e1)
                    {
                        Events.LogEvent(LogEvent.Level.Error, channel_name + ". error: " + e1);
                    }
                    break;
                case "unbind":
                    var unbind = new Unbind(pdu);
                    var unbind_resp = new UnbindResp();
                    unbind_resp.sequence_number = unbind.sequence_number;
                    response = unbind_resp.Encode();
                    SendPDU(response);

                    Events.LogChannelEvent(channel_name, "Sending [unbind_resp]", debug ? response : "");

                    enquire_link_timer.Stop();
                    timeout_timer.Stop();

                    tcp_stream.Close();
                    tcp_client.Close();

                    Gate.Clients.Remove(channel_name);
                    Gate.Servers.Remove(channel_name);

                    Events.LogChannelEvent(channel_name, "Disconnected");

                    // should reconnect if it was a client
                    if (!is_server)
                    {
                        Thread.Sleep(10000);
                        Task.Run(() => { Gate.Clients[channel_name].Connect(); });
                    }
                    break;
                case "unbind_resp":
                    ;
                    break;
                default:
                    Events.LogChannelEvent(channel_name, "Receiving unknown command", pdu);
                    break;
            }
        }

        private void SendEnquireLink()
        {
            string pdu;
            var enquire_link = new EnquireLink();

            enquire_link.sequence_number = sequence_number;
            commands_queue.Add(sequence_number, DateTime.Now);

            pdu = enquire_link.Encode();
            Events.LogChannelEvent(channel_name, "Sending [enquire_link]", debug ? pdu : "");
            SendPDU(pdu);
        }

        /// <summary>
        /// Sends delivery report of the message
        /// </summary>
        /// <param name="messageId"></param>
        /// <param name="status"></param>
        public void SubmitDeliveryReport(string messageId, Common.MessageStatus status)
        {
            string pdu;
            string messageStatus;

            switch (status)
            {
                case Common.MessageStatus.ACK_expired:
                case Common.MessageStatus.delivered_ACK_received:
                case Common.MessageStatus.duplicate_ACK_detected:
                case Common.MessageStatus.received_routed:
                case Common.MessageStatus.sent:
                case Common.MessageStatus.submitted_waiting_for_ACK:
                    messageStatus = "DELIVRD";
                    break;
                case Common.MessageStatus.queued:
                case Common.MessageStatus.received_waiting_to_be_processed:
                case Common.MessageStatus.scheduled:
                    messageStatus = "ACCEPTD";
                    break;
                case Common.MessageStatus.generic_error:
                case Common.MessageStatus.locked_by_system:
                case Common.MessageStatus.NACK_received:
                case Common.MessageStatus.rejected:
                case Common.MessageStatus.out_of_balance:
                    messageStatus = "REJECTD";
                    break;
                case Common.MessageStatus.message_undeliverable:
                case Common.MessageStatus.received_route_failure:
                case Common.MessageStatus.received_no_route_specified:
                case Common.MessageStatus.no_channel_can_handle_message:
                    messageStatus = "UNDELIV";
                    break;
                case Common.MessageStatus.unknown_recipient:
                    messageStatus = "UNKNOWN";
                    break;
                default:
                    messageStatus = "DELIVRD";
                    break;
            }

            var deliver_sm = new DeliverSm(Events);
            deliver_sm.sequence_number = sequence_number;
            deliver_sm.Recipient = "";
            deliver_sm.Sender = "";
            deliver_sm.BodyFormat = "ascii";
            deliver_sm.ESMClass = 4;
            deliver_sm.IsDeliveryReport = true;
            var short_message = new StringBuilder();
            short_message.Append("id:" + messageId);
            short_message.Append(" sub:001");
            if (messageStatus == "DELIVRD")
            {
                short_message.Append(" dlvrd:001");
            }
            else
            {
                short_message.Append(" dlvrd:000");
            }
            short_message.Append(" submit date:" + DateTime.Now.ToString("yyMMddHHmm"));
            short_message.Append(" done date:" + DateTime.Now.ToString("yyMMddHHmm"));
            short_message.Append(" stat:" + messageStatus);
            short_message.Append(" err:0");
            short_message.Append(" text:");
            deliver_sm.Body = short_message.ToString();
            pdu = deliver_sm.EncodeM()[0];

            Events.LogChannelEvent(channel_name, "Sending [deliver_sm] report", debug ? pdu : "");
            SendPDU(pdu);
        }

        /// <summary>
        /// Closes connection, sends unbind command, stops timers
        /// </summary>
        public void Quit()
        {
            enquire_link_timer.Stop();
            timeout_timer.Stop();

            if (!connected)
            {
                KillConnection();
                return;
            }

            connected = false;

            var unbind = new Unbind();
            unbind.sequence_number = sequence_number;
            string pdu = unbind.Encode();
            SendPDU(pdu);
            Events.LogChannelEvent(channel_name, "Sending [unbind]", debug ? pdu : "");

            commands_queue.Clear();
            submitted_messages.Clear();
            multipart_messages.Clear();

            KillConnection();
        }

        private void KillConnection()
        {
            if (tcp_client != null)
            {
                try
                {
                    tcp_client.Close();
                }
                catch (SocketException)
                {
                    // can be already closed/dropped
                }
            }

            if (tcp_stream != null)
            {
                try
                {
                    tcp_stream.Close();
                }
                catch (SocketException)
                {
                    // can be already closed/dropped
                }
            }

            tcp_stream = null;
            tcp_client = null;

            if (!is_server)
            {
                Gate.Clients.Remove(channel_name);
            }
            else
            {
                Gate.Servers.Remove(channel_name);
            }
        }

        /// <summary>
        /// Sends message
        /// </summary>
        /// <param name="message_id">Unique message id</param>
        /// <param name="sender">Sender</param>
        /// <param name="recipient">Recipient</param>
        /// <param name="body">body</param>
        /// <param name="body_format">Body format. unicode,ascii,wap_push</param>
        /// <param name="delivery_report">Request delivery report</param>
        /// <returns>0 if successful, 
        /// 1 - No active connection, 
        /// 2 - Too many messages per second
        /// 3 - Duplicate sequence number </returns>
        public virtual int SubmitSm(int message_id, string sender, string recipient, string body, string body_format = "ascii", bool delivery_report = true)
        {
            if (!connected)
            {
                Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.received_route_failure);
                return 1;
            }

            // check for max sms count
            if (max_threads != 0)
            {
                int now_second = DateTime.Now.Second;
                if (now_second != last_second)
                {
                    last_second = now_second;
                    sent_during_last_second = 0;
                }
                else
                {
                    sent_during_last_second++;
                }
                if (max_threads < sent_during_last_second)
                {
                    // messages should be resubmitted later
                    Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);

                    return 2; // too many SMS per second
                }
            }

            var submit_sm = new SubmitSm();

            while (submitted_messages.ContainsKey(sequence_number))
            {
                sequence_number++;
            }

            submit_sm.sequence_number = sequence_number;
            var sm = new submitted_message();
            sm.message_id = message_id;
            sm.submitted_time = DateTime.Now;
            sm.registered_delivery = delivery_report;
            try
            {
                submitted_messages.Add(submit_sm.sequence_number, sm);
            }
            catch (ArgumentException)
            {
                Events.LogChannelEvent(channel_name, "Duplicate sequence number.");
                Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);
                return 3;
            }

            submit_sm.Sender = sender;
            submit_sm.Recipient = recipient;
            submit_sm.Body = body;
            submit_sm.BodyFormat = body_format;
            submit_sm.Is8bit = use8bit;

            submit_sm.ServiceType = system_type;

            submit_sm.SourceAddrTON = source_ton;
            submit_sm.SourceAddrNPI = source_npi;

            submit_sm.DestAddrTON = destination_ton;
            submit_sm.DestAddrNPI = destination_npi;

            submit_sm.SplitBaseASCII = split_base_ascii;

            submit_sm.RegisteredDelivery = delivery_report ? 1 : 0;

            submit_sm.LargeMessageHandleMethod = large_message;
            multipart_sequence_number++;
            if (multipart_sequence_number == 255)
            {
                multipart_sequence_number = 1;
            }
            submit_sm.MultipartSequenceNumber = multipart_sequence_number;
            string[] pdu = submit_sm.EncodeM();

            Events.LogChannelEvent(channel_name, "Sending from " + sender + " to " + recipient);

            var k = pdu.Length;
            foreach (var part in pdu)
            {
                Events.LogChannelEvent(channel_name, "Sending [submit_sm]", debug ? part : "");

                SendPDU(part);
                if (k != 1)
                {
                    Thread.Sleep(multypart_delay);
                }
                k--;

                if (registered_delivery == 1 && delivery_report)
                {
                    Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.submitted_waiting_for_ACK);
                }
                else
                {
                    Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.received_routed);
                }
            }

            return 0;
        }
    }
}
