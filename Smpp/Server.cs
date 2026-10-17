using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Threading;
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
        private static CancellationTokenSource listenerCancellation;
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
            body_format = MessageBodyCodec.SelectFormat(body, body_format);
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
            => StartAsync(ip, port, null);

        /// <summary>Starts an SMPP listener with optional TLS, authenticating TLS before reading bind credentials.</summary>
        public static void StartAsync(string ip, int port, SslServerAuthenticationOptions tlsOptions)
        {
            if (!IPAddress.TryParse(ip, out var address))
                throw new ArgumentException("Check IP address", nameof(ip));
            if (tlsOptions != null && tlsOptions.ServerCertificate == null && tlsOptions.ServerCertificateContext == null &&
                tlsOptions.ServerCertificateSelectionCallback == null)
                throw new ArgumentException("TLS listeners require a server certificate or a certificate selection callback.", nameof(tlsOptions));
            lock (ListenerLock)
            {
                if (running)
                    throw new InvalidOperationException("The SMPP listener is already running.");
                var listener = new TcpListener(address, port);
                listener.Start();
                tcp_server = listener;
                running = true;
                listenerCancellation = new CancellationTokenSource();
                _ = AcceptConnections(listener, listenerCancellation.Token, tlsOptions);
            }
        }

        private static Task AcceptConnections(TcpListener listener, CancellationToken cancellation, SslServerAuthenticationOptions tlsOptions) =>
            RunAcceptLoop(listener.AcceptTcpClientAsync,
                client => { _ = CreateConnection(listener, client, cancellation, tlsOptions: tlsOptions); },
                ex =>
                {
                    Server[] servers;
                    lock (Gate.Servers)
                        servers = Gate.Servers.Values.ToArray();
                    foreach (var server in servers)
                        server.Notify(() => server.Events.LogChannelEvent(server.channel_name, "Accept failed; retrying: " + ex.Message));
                }, cancellation);

        internal static async Task RunAcceptLoop(Func<CancellationToken, ValueTask<TcpClient>> accept,
            Action<TcpClient> accepted, Action<SocketException> failed, CancellationToken cancellation)
        {
            while (!cancellation.IsCancellationRequested)
            {
                try
                {
                    var client = await accept(cancellation).ConfigureAwait(false);
                    if (cancellation.IsCancellationRequested)
                    {
                        client.Dispose();
                        return;
                    }
                    accepted(client);
                }
                catch (SocketException ex)
                {
                    if (cancellation.IsCancellationRequested)
                        return;
                    failed(ex);
                    // Resource exhaustion must not turn retries into a busy loop.
                    try { await Task.Delay(100, cancellation).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        internal static async Task CreateConnection(TcpListener listener, TcpClient client,
            CancellationToken cancellation, int bindTimeoutMilliseconds = 10000, SslServerAuthenticationOptions tlsOptions = null)
        {
            bool accepted = false;
            System.IO.Stream stream = null;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            deadline.CancelAfter(bindTimeoutMilliseconds);
            try
            {
                stream = client.GetStream();
                stream.WriteTimeout = 10000;
                if (tlsOptions != null)
                {
                    var secure = new SslStream(stream, false);
                    stream = secure;
                    await secure.AuthenticateAsServerAsync(tlsOptions, deadline.Token).ConfigureAwait(false);
                }
                var header = new byte[4];
                await stream.ReadExactlyAsync(header, deadline.Token).ConfigureAwait(false);
                uint length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
                if (length < 16 || length > Common.MAX_BIND_PDU_LENGTH)
                    return;
                var bytes = new byte[length];
                Buffer.BlockCopy(header, 0, bytes, 0, header.Length);
                await stream.ReadExactlyAsync(bytes.AsMemory(header.Length), deadline.Token).ConfigureAwait(false);
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
                }
                if (server == null)
                {
                    response.command_status = 13; // ESME_RBINDFAIL
                    await stream.WriteAsync(Common.ConvertHexStringToByteArray(response.Encode()), deadline.Token).ConfigureAwait(false);
                    return;
                }
                deadline.Token.ThrowIfCancellationRequested();
                accepted = server.AcceptSession(client, stream, response, command);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is SocketException ||
                ex is ArgumentException || ex is FormatException || ex is IndexOutOfRangeException ||
                ex is Smpp.Exceptions.CommandLengthException || ex is ObjectDisposedException || ex is OperationCanceledException ||
                ex is AuthenticationException)
            {
                // Invalid or incomplete bind frames never become registered sessions.
            }
            finally
            {
                if (!accepted)
                {
                    stream?.Dispose();
                    client.Dispose();
                }
            }
        }

        private bool AcceptSession(TcpClient client, System.IO.Stream stream, Pdu response, Common.CommandId command)
        {
            lock (SessionLock)
            {
                if (stopped || tcp_stream != null)
                {
                    response.command_status = 5; // ESME_RALYBND: do not overwrite a live session.
                    stream.Write(Common.ConvertHexStringToByteArray(response.Encode()));
                    return false;
                }
                stream.Write(Common.ConvertHexStringToByteArray(response.Encode()));
                BeginSession(client, true, stream);
                string remote = client.Client.RemoteEndPoint.ToString();
                Notify(() => Events.LogChannelEvent(channel_name, "Received [" + command + "]"));
                Notify(() => Events.LogChannelEvent(channel_name, "Logged in successfully IP: " + remote));
                Receive();
                return true;
            }
        }

        public static bool Stop()
        {
            lock (ListenerLock)
            {
                running = false;
                listenerCancellation?.Cancel();
                listenerCancellation?.Dispose();
                listenerCancellation = null;
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
