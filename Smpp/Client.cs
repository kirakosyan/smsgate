using System;
using System.Net.Sockets;
using Smpp.Events;
using Smpp.Requests;

namespace Smpp
{
    public class Client : Connection
    {
        public Client(string channelName, GateEvents events) : base(channelName, events)
        {
            lock (Gate.Clients)
            {
                if (Gate.Clients.ContainsKey(channelName))
                    throw new ArgumentException("Channel with the same name already registered");
                Gate.Clients.Add(channelName, this);
            }
        }

        /// <summary>Opens the transport and sends a bind. connected becomes true only after a successful bind response.</summary>
        public bool Connect()
        {
            lock (SessionLock)
            {
                if (stopped)
                    return false;
                if (tcp_stream != null)
                    return true;
                if (!direction_in && !direction_out)
                    throw new InvalidOperationException("At least one connection direction must be enabled.");
                CancelReconnect();
                var transport = new TcpClient();
                try
                {
                    transport.ConnectAsync(host, port).WaitAsync(TimeSpan.FromMilliseconds(timeout)).GetAwaiter().GetResult();
                    BeginSession(transport, false);
                }
                catch (Exception ex) when (ex is SocketException || ex is TimeoutException)
                {
                    transport.Dispose();
                    Events.LogChannelEvent(channel_name, "Unable to connect to host: " + host + ":" + port);
                    Disconnect(true);
                    return false;
                }
                Events.LogChannelEvent(channel_name, "Connected to " + host + ":" + port);
                int result;
                if (direction_in && direction_out)
                {
                    result = SendBind(new BindTransceiver
                    {
                        SystemID = system_id, Password = password, SystemType = system_type,
                        Ton = (Common.TonType)source_ton, Npi = (Common.NpiType)source_npi
                    }, Common.CommandId.bind_transceiver_resp, "bind_transceiver");
                }
                else if (direction_out)
                {
                    result = SendBind(new BindTransmitter
                    {
                        SystemID = system_id, Password = password, SystemType = system_type,
                        Ton = (Common.TonType)source_ton, Npi = (Common.NpiType)source_npi
                    }, Common.CommandId.bind_transmitter_resp, "bind_transmitter");
                }
                else
                {
                    result = SendBind(new BindReceiver
                    {
                        SystemID = system_id, Password = password, SystemType = system_type,
                        Ton = (Common.TonType)source_ton, Npi = (Common.NpiType)source_npi
                    }, Common.CommandId.bind_receiver_resp, "bind_receiver");
                }
                if (result != 0)
                    return false;
                Receive();
                return true;
            }
        }
    }
}
