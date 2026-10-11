using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Smpp.Events;
using Smpp.Requests;
using Smpp.Responses;

namespace Smpp
{
    public class Server : Connection
    {
        private static readonly object ListenerLock = new object();
        private static TcpListener tcp_server;
        public static volatile bool running;

        public Server(string channelName, GateEvents events) : base(channelName, events)
        {
            is_server = true;
            lock (Gate.Servers)
            {
                if (Gate.Servers.ContainsKey(channelName))
                    throw new ArgumentException("Channel with the same name already registered");
                Gate.Servers.Add(channelName, this);
            }
        }

        public override int SubmitSm(int message_id, string sender, string recipient, string body,
            string body_format = "ascii", bool delivery_report = true)
        {
            bool deliver = use_deliver_sm;
            return SendMessage(message_id, sender, recipient, delivery_report, deliver ? "deliver_sm" : "data_sm",
                (sequence, reference) =>
                {
                    if (deliver)
                        return new DeliverSm(Events)
                        {
                            sequence_number = sequence, MultipartSequenceNumber = reference,
                            Sender = sender, Recipient = recipient, Body = body, BodyFormat = body_format,
                            ServiceType = system_type, SourceAddrTON = source_ton, SourceAddrNPI = source_npi,
                            DestAddrTON = destination_ton, DestAddrNPI = destination_npi,
                            RegisteredDelivery = registered_delivery == 1 && delivery_report ? 1 : 0,
                            SplitBaseASCII = split_base_ascii, LargeMessageHandleMethod = large_message, Is8bit = use8bit
                        }.EncodeM();
                    return new DataSm
                    {
                        sequence_number = sequence, Sender = sender, Recipient = recipient, Body = body,
                        BodyFormat = body_format, ServiceType = system_type, SourceAddrTON = source_ton,
                        SourceAddrNPI = source_npi, DestAddrTON = destination_ton, DestAddrNPI = destination_npi,
                        RegisteredDelivery = registered_delivery == 1 && delivery_report ? 1 : 0
                    }.EncodeM();
                });
        }

        /// <summary>Starts the listener before returning, then accepts sessions in the background.</summary>
        public static void StartAsync(string ip, int port)
        {
            if (!IPAddress.TryParse(ip, out var address))
                throw new ArgumentException("Check IP address", nameof(ip));
            lock (ListenerLock)
            {
                if (running)
                    throw new InvalidOperationException("The SMPP listener is already running.");
                var listener = new TcpListener(address, port);
                listener.Start();
                tcp_server = listener;
                running = true;
                _ = AcceptConnections(listener);
            }
        }

        private static async Task AcceptConnections(TcpListener listener)
        {
            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = Task.Run(() => CreateConnection(listener, client));
                }
            }
            catch (SocketException) { } // Stop interrupts the pending accept.
            catch (ObjectDisposedException) { }
        }

        private static void CreateConnection(TcpListener listener, TcpClient client)
        {
            bool accepted = false;
            try
            {
                client.ReceiveTimeout = 10000;
                var stream = client.GetStream();
                stream.ReadTimeout = 10000;
                stream.WriteTimeout = 10000;
                var header = new byte[4];
                if (!ReadExact(stream, header, 0, header.Length))
                    return;
                int length = int.Parse(Common.ConvertByteArrayToHexString(header), System.Globalization.NumberStyles.HexNumber);
                if (length < 16 || length > Common.MAX_BIND_PDU_LENGTH)
                    return;
                var bytes = new byte[length];
                Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
                if (!ReadExact(stream, bytes, header.Length, length - header.Length))
                    return;
                string pdu = Common.ConvertByteArrayToHexString(bytes);
                var command = (Common.CommandId)uint.Parse(pdu.Substring(8, 8), System.Globalization.NumberStyles.HexNumber);
                string login;
                string password;
                Pdu response;
                switch (command)
                {
                    case Common.CommandId.bind_receiver:
                        var receiver = new BindReceiver(pdu);
                        login = receiver.SystemID;
                        password = receiver.Password;
                        response = new BindReceiverResp { sequence_number = receiver.sequence_number };
                        break;
                    case Common.CommandId.bind_transmitter:
                        var transmitter = new BindTransmitter(pdu);
                        login = transmitter.SystemID;
                        password = transmitter.Password;
                        response = new BindTransmitterResp { sequence_number = transmitter.sequence_number };
                        break;
                    case Common.CommandId.bind_transceiver:
                        var transceiver = new BindTransceiver(pdu);
                        login = transceiver.SystemID;
                        password = transceiver.Password;
                        response = new BindTransceiverResp { sequence_number = transceiver.sequence_number };
                        break;
                    default:
                        return;
                }
                Server server;
                lock (Gate.Servers)
                    server = Gate.Servers.Values.FirstOrDefault(value => value.system_id == login && value.password == password);
                lock (ListenerLock)
                {
                    if (!running || !ReferenceEquals(tcp_server, listener))
                        return;
                    if (server == null)
                    {
                        response.command_status = 13; // ESME_RBINDFAIL
                        stream.Write(Common.ConvertHexStringToByteArray(response.Encode()));
                        return;
                    }
                }
                accepted = server.AcceptSession(client, response, command);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException ||
                ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException ||
                ex is Smpp.Exceptions.CommandLengthException || ex is ObjectDisposedException)
            {
                // Invalid or incomplete bind frames never become registered sessions.
            }
            finally
            {
                if (!accepted)
                    client.Dispose();
            }
        }

        private bool AcceptSession(TcpClient client, Pdu response, Common.CommandId command)
        {
            lock (SessionLock)
            {
                if (stopped || tcp_stream != null)
                {
                    response.command_status = 5; // ESME_RALYBND: do not overwrite a live session.
                    client.GetStream().Write(Common.ConvertHexStringToByteArray(response.Encode()));
                    return false;
                }
                client.GetStream().Write(Common.ConvertHexStringToByteArray(response.Encode()));
                BeginSession(client, true);
                Events.LogChannelEvent(channel_name, "Received [" + command + "]");
                Events.LogChannelEvent(channel_name, "Logged in successfully IP: " + client.Client.RemoteEndPoint);
                Receive();
                return true;
            }
        }

        private static bool ReadExact(NetworkStream stream, byte[] buffer, int offset, int count)
        {
            while (count > 0)
            {
                int read = stream.Read(buffer, offset, count);
                if (read == 0)
                    return false;
                offset += read;
                count -= read;
            }
            return true;
        }

        public static bool Stop()
        {
            lock (ListenerLock)
            {
                running = false;
                tcp_server?.Stop();
                tcp_server = null;
            }
            KillAll();
            return true;
        }

        public static void KillAll()
        {
            Server[] servers;
            lock (Gate.Servers)
                servers = Gate.Servers.Values.ToArray();
            foreach (var server in servers)
                server.Quit();
        }
    }
}
