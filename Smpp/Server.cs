using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Smpp.Events;
using Smpp.Exceptions;
using Smpp.Requests;
using Smpp.Responses;

namespace Smpp
{
    public class Server : Connection
    {
        private const int ESME_RBINDFAIL = 13;

        private static TcpListener tcp_server;
        public static Boolean running = false;

        public Server(string channelName, GateEvents events)
            : base(channelName, events)
        {
            if (Gate.Servers.ContainsKey(channelName))
            {
                throw new ArgumentException("Channel with the same name already registered");
            }

            Gate.Servers.Add(channelName, this);
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
        public override int SubmitSm(int message_id, string sender, string recipient, string body, string body_format = "ascii", bool delivery_report = true)
        {
            if (!use_deliver_sm)
            {
                return DataSm(message_id, sender, recipient, body, body_format, delivery_report);
            }
            return DeliverSm(message_id, sender, recipient, body, body_format, delivery_report);
        }

        private int DataSm(int message_id, string sender, string recipient, string body, string body_format, bool delivery_report)
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
                    Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);

                    return 2; // too many SMS per second
                }
            }

            var data_sm = new DataSm();

            while (submitted_messages.ContainsKey(sequence_number))
            {
                sequence_number++;
            }

            data_sm.sequence_number = sequence_number;
            var sm = new submitted_message();
            sm.message_id = message_id;
            sm.submitted_time = DateTime.Now;
            sm.registered_delivery = delivery_report;
            try
            {
                submitted_messages.Add(data_sm.sequence_number, sm);
            }
            catch (ArgumentException)
            {
                Events.LogChannelEvent(channel_name, "Duplicate sequence number.");
                Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);
                return 3;
            }

            data_sm.Sender = sender;
            data_sm.Recipient = recipient;
            data_sm.Body = body;
            data_sm.BodyFormat = body_format;

            data_sm.ServiceType = system_type;

            data_sm.SourceAddrTON = source_ton;
            data_sm.SourceAddrNPI = source_npi;

            data_sm.DestAddrTON = destination_ton;
            data_sm.DestAddrNPI = destination_npi;

            if (registered_delivery == 1 && delivery_report)
            {
                data_sm.RegisteredDelivery = registered_delivery;
            }
            else
            {
                data_sm.RegisteredDelivery = 0;
            }

            var pdu = data_sm.EncodeM();

            Events.LogChannelEvent(channel_name, "Sending from " + sender + " to " + recipient);

            var retVal = 0;
            var k = pdu.Length;
            foreach (string part in pdu)
            {
                Events.LogChannelEvent(channel_name, "Sending [data_sm]", debug ? part : "");

                retVal += SendPDU(part);
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

        private int DeliverSm(int message_id, string sender, string recipient, string body, string body_format, bool delivery_report)
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
                    Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);

                    return 2; // too many SMS per second
                }
            }

            var deliver_sm = new DeliverSm(Events);

            while (submitted_messages.ContainsKey(sequence_number))
            {
                sequence_number++;
            }

            deliver_sm.sequence_number = sequence_number;
            submitted_message sm = new submitted_message();
            sm.message_id = message_id;
            sm.submitted_time = DateTime.Now;
            sm.registered_delivery = delivery_report;
            try
            {
                submitted_messages.Add(deliver_sm.sequence_number, sm);
            }
            catch (ArgumentException)
            {
                Events.LogChannelEvent(channel_name, "Duplicate sequence number.");
                Events.LogMessageChangeStatusEvent(message_id, Common.MessageStatus.queued);
                return 3;
            }

            deliver_sm.Sender = sender;
            deliver_sm.Recipient = recipient;
            deliver_sm.Body = body;
            deliver_sm.BodyFormat = body_format;

            deliver_sm.ServiceType = system_type;

            deliver_sm.SourceAddrTON = source_ton;
            deliver_sm.SourceAddrNPI = source_npi;

            deliver_sm.DestAddrTON = destination_ton;
            deliver_sm.DestAddrNPI = destination_npi;

            deliver_sm.SplitBaseASCII = split_base_ascii;
            deliver_sm.LargeMessageHandleMethod = large_message;

            if (registered_delivery == 1 && delivery_report)
            {
                deliver_sm.RegisteredDelivery = registered_delivery;
            }
            else
            {
                deliver_sm.RegisteredDelivery = 0;
            }

            string[] pdu = deliver_sm.EncodeM();

            Events.LogChannelEvent(channel_name, "Sending from " + sender + " to " + recipient);

            int k = pdu.Length;
            foreach (string part in pdu)
            {
                Events.LogChannelEvent(channel_name, "Sending [deliver_sm]", debug ? part : "");

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

        /// <summary>
        /// Starts Server instance, should run only once.
        /// </summary>
        /// <param name="ip">IP address to listen on</param>
        /// <param name="port">Port number, default 2775</param>
        /// <returns>True if server was successfully started</returns>
        public static void StartAsync(string ip, int port)
        {
            Task.Run(() => { Start(ip, port); });
        }

        /// <summary>
        /// Starts Server instance, should run only once.
        /// </summary>
        /// <param name="ip">IP address to listen on</param>
        /// <param name="port">Port number, default 2775</param>
        /// <returns>True if server was successfully started</returns>
        private static bool Start(string ip, int port)
        {
            IPAddress ip_address;

            if (!IPAddress.TryParse(ip, out ip_address))
            {
                throw new ArgumentException("Check IP address");
            }

            tcp_server = new TcpListener(ip_address, port);

            tcp_server.Start();
            running = true;

            while (running)
            {
                try
                {
                    if (running && tcp_server.Pending())
                    {
                        new Thread(CreateConnection).Start();
                    }
                    Thread.Sleep(500);
                }
                catch
                {
                    break;
                }
            }

            return true;
        }

        /// <summary>
        /// Handles incoming request
        /// </summary>
        private static void CreateConnection()
        {
            TcpClient tcp_client;

            try { tcp_client = tcp_server.AcceptTcpClient(); }
            catch { return; }

            try
            {
                if (tcp_client != null && tcp_client.Connected)
                {
                    NetworkStream ns;
                    ns = tcp_client.GetStream();
                    byte[] buffer = new byte[Common.MAX_RECEIVE_LENGTH];
                    byte[] ByteLength = new byte[4];
                    byte[] BytePDU;
                    int PDULength;
                    string pdu;
                    string systemId;
                    string response = "";
                    int l = 0;

                    string ip = ((IPEndPoint)tcp_client.Client.RemoteEndPoint).Address.ToString();
                    int authCode = 0;

                    ns.Read(buffer, 0, buffer.Length);
                    ByteLength[0] = buffer[0]; ByteLength[1] = buffer[1];
                    ByteLength[2] = buffer[2]; ByteLength[3] = buffer[3];
                    PDULength = int.Parse(Common.ConvertByteArrayToHexString(ByteLength), System.Globalization.NumberStyles.HexNumber);

                    BytePDU = new byte[PDULength];
                    for (int k = 0; k < PDULength; k++)
                    {
                        try
                        {
                            BytePDU[k] = buffer[l++];
                        }
                        catch
                        {
                            ns.Close();
                            tcp_client.Close();
                            throw new CommandLengthException(String.Empty);
                        }
                    }
                    pdu = Common.ConvertByteArrayToHexString(BytePDU);

                    switch (Common.command_id.Values[Common.command_id.IndexOfKey(uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber))])
                    {
                        case "bind_receiver":
                            var bind_receiver = new BindReceiver(pdu);
                            var bind_receiver_resp = new BindReceiverResp();

                            authCode = Authenticate(bind_receiver.SystemID, bind_receiver.Password, ip);
                            systemId = bind_receiver.SystemID;
                            bind_receiver_resp.SystemID = Common.SMSC_ID;
                            bind_receiver_resp.command_status = (uint)authCode;
                            bind_receiver_resp.sequence_number = bind_receiver.sequence_number;
                            response = bind_receiver_resp.Encode();

                            break;
                        case "bind_transmitter":
                            var bind_transmitter = new BindTransmitter(pdu);
                            var bind_transmitter_resp = new BindTransmitterResp();

                            authCode = Authenticate(bind_transmitter.SystemID, bind_transmitter.Password, ip);
                            systemId = bind_transmitter.SystemID;
                            bind_transmitter_resp.SystemID = Common.SMSC_ID;
                            bind_transmitter_resp.command_status = (uint)authCode;
                            bind_transmitter_resp.sequence_number = bind_transmitter.sequence_number;
                            response = bind_transmitter_resp.Encode();

                            break;
                        case "bind_transceiver":
                            var bind_transceiver = new BindTransceiver(pdu);
                            var bind_transceiver_resp = new BindTransceiverResp();

                            authCode = Authenticate(bind_transceiver.SystemID, bind_transceiver.Password, ip);
                            systemId = bind_transceiver.SystemID;
                            bind_transceiver_resp.SystemID = Common.SMSC_ID;
                            bind_transceiver_resp.command_status = (uint)authCode;
                            bind_transceiver_resp.sequence_number = bind_transceiver.sequence_number;
                            response = bind_transceiver_resp.Encode();

                            break;
                        default:
                            // Something unknown was received
                            ns.Close();
                            tcp_client.Close();
                            return;
                    }
                    byte[] msg;
                    msg = Common.ConvertHexStringToByteArray(response);

                    ns.Write(msg, 0, msg.Length);

                    if (authCode != 0)
                    {
                        // Events.LogEvent(LogEvent.Level.Error, "Received [" + Common.command_id.Values[Common.command_id.IndexOfKey(uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber))] + "]");
                        // Events.LogEvent(LogEvent.Level.Error, "Error connecting: " + Common.command_status[(uint)authCode].description);
                        ns.Close();
                        tcp_client.Close();
                        return;
                    }

                    Server esme = Gate.Servers.Values.FirstOrDefault(server => server.system_id == systemId);
                    esme.Events.LogChannelEvent(esme.channel_name, "Received [" + Common.command_id.Values[Common.command_id.IndexOfKey(uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber))] + "]");

                    esme.Events.LogChannelEvent(esme.channel_name, "Logged in successfully IP: " + ip);

                    esme.tcp_client = tcp_client;
                    esme.tcp_stream = tcp_client.GetStream();
                    esme.is_server = true;
                    esme.connected = true;
                    esme.enquire_link_timer.Start();
                    esme.timeout_timer.Start();
                    esme.Receive();
                }
            }
            catch (Exception)
            {
                return;
            }
        }

        private static int Authenticate(string login, string password, string ip)
        {
            int authCode = ESME_RBINDFAIL;

            if (Gate.Servers.Values.Any(server => server.system_id == login && server.password == password))
            {
                authCode = 0; // ESME_ROK
            }

            // check firewall
            // the IP is in parameter and can be checked for firewall rules here

            return authCode;
        }

        public static bool Stop()
        {
            running = false;

            KillAll();

            try
            {
                tcp_server.Stop();
            }
            catch
            {
                return false;
            }

            return true;
        }


        /// <summary>
        /// Closes all connection
        /// </summary>
        public static void KillAll()
        {
            var connections = Gate.Servers.Keys.ToList();

            foreach (string c in connections)
            {
                try
                {
                    Gate.Servers[c].Quit();
                }
                catch
                {; }
            }
            connections.Clear();
        }
    }
}
