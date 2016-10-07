using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Smpp.Events;
using Smpp.Requests;

namespace Smpp
{
    public class Client : Connection
    {
        /// <summary>
        /// Initializes new object for Client connection
        /// </summary>
        /// <param name="channelName">Unique channel name</param>
        /// <param name="events">Events object</param>
        public Client(string channelName, GateEvents events) : base(channelName, events)
        {
            if (Gate.Clients.ContainsKey(channelName))
            {
                throw new ArgumentException("Channel with the same name already registered");
            }

            Gate.Clients.Add(channelName, this);
        }

        /// <summary>
        /// Starts binding with server
        /// </summary>
        /// <returns>True if connection was successful, false otherwise</returns>
        public bool Connect()
        {
            // need to check when reconnecting
            if (tcp_client != null && tcp_client.Connected)
            {
                tcp_client.Close();
            }

            tcp_client = new TcpClient();
            try
            {
                tcp_client.Connect(host, port);
                tcp_stream = tcp_client.GetStream();
                Events.LogChannelEvent(channel_name, "Connected to " + host + ":" + port);

                // Start communication
                Receive();
            }
            catch (SocketException)
            {
                Events.LogChannelEvent(channel_name, "Unable to connect to host: " + host + ":" + port);

                // Try to reconnect in 10 sec
                Thread.Sleep(10000);
                Task.Run(() => { Connect(); });

                return false;
            }

            enquire_link_timer.Start();
            timeout_timer.Start();

            if (direction_in && direction_out)
            {
                SendBindTransceiver();
            }
            if (direction_out && !direction_in)
            {
                SendBindTransmitter();
            }
            if (direction_in && !direction_out)
            {
                SendBindReceiver();
            }

            return true;
        }

        private void SendBindTransceiver()
        {
            string pdu;

            var bind_transceiver = new BindTransceiver
            {
                SystemID = system_id,
                Password = password,
                InterfaceVersion = Common.SmppVersionType.Version3_4,
                SystemType = system_type,
                Ton = (Common.TonType)source_ton,
                Npi = (Common.NpiType)source_npi,
                sequence_number = sequence_number
            };

            commands_queue.Add(sequence_number, DateTime.Now);

            pdu = bind_transceiver.Encode();
            SendPDU(pdu);
            Events.LogChannelEvent(channel_name, "Sending [bind_transceiver]", debug ? pdu : "");
        }

        private void SendBindTransmitter()
        {
            string pdu;
            var bind_transmitter = new BindTransmitter
            {
                SystemID = system_id,
                Password = password,
                InterfaceVersion = Common.SmppVersionType.Version3_4,
                SystemType = system_type,
                Ton = (Common.TonType)source_ton,
                Npi = (Common.NpiType)source_npi,
                sequence_number = sequence_number
            };

            commands_queue.Add(sequence_number, DateTime.Now);

            pdu = bind_transmitter.Encode();
            SendPDU(pdu);
            Events.LogChannelEvent(channel_name, "Sending [bind_transmitter]", debug ? pdu : "");
        }

        private void SendBindReceiver()
        {
            var bind_receiver = new BindReceiver
            {
                SystemID = system_id,
                Password = password,
                InterfaceVersion = Common.SmppVersionType.Version3_4,
                SystemType = system_type,
                Ton = (Common.TonType)source_ton,
                Npi = (Common.NpiType)source_npi,
                sequence_number = sequence_number
            };

            commands_queue.Add(sequence_number, DateTime.Now);

            string pdu = bind_receiver.Encode();
            SendPDU(pdu);
            Events.LogChannelEvent(channel_name, "Sending [bind_receiver]", debug ? pdu : "");
        }
    }
}
