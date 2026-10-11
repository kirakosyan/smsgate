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
        private CancellationTokenSource connectCancellation;
        private Task<bool> connecting;

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
        public bool Connect() => ConnectAsync().GetAwaiter().GetResult();

        /// <summary>Asynchronously opens the transport. Concurrent callers share one cancellable attempt.</summary>
        public Task<bool> ConnectAsync()
        {
            lock (SessionLock)
            {
                if (stopped)
                    return Task.FromResult(false);
                if (tcp_stream != null)
                    return Task.FromResult(true);
                if (connecting != null)
                    return connecting;
                if (!direction_in && !direction_out)
                    throw new InvalidOperationException("At least one connection direction must be enabled.");
                CancelReconnect();
                var cancellation = new CancellationTokenSource(timeout);
                connectCancellation = cancellation;
                connecting = Task.Run(() => ConnectCoreAsync(cancellation));
                return connecting;
            }
        }

        protected virtual ValueTask ConnectTransportAsync(TcpClient transport, CancellationToken cancellation) =>
            transport.ConnectAsync(host, port, cancellation);

        protected override void CancelConnectionAttempt() => connectCancellation?.Cancel();

        private async Task<bool> ConnectCoreAsync(CancellationTokenSource cancellation)
        {
            var transport = new TcpClient();
            bool installed = false;
            bool retry = false;
            try
            {
                await ConnectTransportAsync(transport, cancellation.Token).ConfigureAwait(false);
                lock (SessionLock)
                {
                    if (stopped || !ReferenceEquals(connectCancellation, cancellation))
                        return false;
                    if (cancellation.IsCancellationRequested)
                    {
                        retry = true;
                        return false;
                    }
                    BeginSession(transport, false);
                    installed = true;
                    Notify(() => Events.LogChannelEvent(channel_name, "Connected to " + host + ":" + port));
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
                    {
                        retry = true;
                        return false;
                    }
                    Receive();
                    return true;
                }
            }
            catch (Exception ex) when (ex is SocketException || ex is OperationCanceledException || ex is ObjectDisposedException)
            {
                lock (SessionLock)
                {
                    retry = !stopped;
                    if (retry)
                        Notify(() => Events.LogChannelEvent(channel_name, "Unable to connect to host: " + host + ":" + port));
                }
                return false;
            }
            finally
            {
                if (!installed)
                    transport.Dispose();
                lock (SessionLock)
                {
                    if (ReferenceEquals(connectCancellation, cancellation))
                    {
                        connectCancellation = null;
                        connecting = null;
                    }
                    cancellation.Dispose();
                    if (retry)
                        ScheduleReconnect();
                }
            }
        }
    }
}
