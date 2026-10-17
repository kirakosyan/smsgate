using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Smpp.Events;
using static Smpp.Tests.ReviewTestSupport;

namespace Smpp.Tests
{
    [TestFixture, NonParallelizable]
    public class TlsTests
    {
        internal sealed class Certificates : IDisposable
        {
            internal X509Certificate2 Root { get; }
            internal X509Certificate2 Server { get; }
            internal Certificates()
            {
                using var rootKey = RSA.Create(2048);
                var rootRequest = new CertificateRequest("CN=SMSGate test root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
                rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign, true));
                Root = rootRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(2));
                using var serverKey = RSA.Create(2048);
                var request = new CertificateRequest("CN=localhost", serverKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
                request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
                request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, true));
                var names = new SubjectAlternativeNameBuilder();
                names.AddDnsName("localhost");
                request.CertificateExtensions.Add(names.Build());
                using var certificate = request.Create(Root, DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(1), RandomNumberGenerator.GetBytes(16));
                using var withKey = certificate.CopyWithPrivateKey(serverKey);
                // Import an independent key handle: SChannel must not depend on a disposed creation key.
                Server = X509CertificateLoader.LoadPkcs12(withKey.Export(X509ContentType.Pfx), null);
            }

            internal SslClientAuthenticationOptions ClientOptions(string targetHost = "localhost", bool trust = true)
            {
                var policy = new X509ChainPolicy
                {
                    RevocationMode = X509RevocationMode.NoCheck,
                    TrustMode = X509ChainTrustMode.CustomRootTrust
                };
                if (trust) policy.CustomTrustStore.Add(Root);
                return new SslClientAuthenticationOptions { TargetHost = targetHost, CertificateChainPolicy = policy };
            }
            public void Dispose() { Server.Dispose(); Root.Dispose(); }
        }

        private static int ListenerPort() => ((IPEndPoint)((TcpListener)typeof(Server)
            .GetField("tcp_server", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null)).LocalEndpoint).Port;

        [Test]
        public async Task ValidatedTlsCarriesGsmMultipartAcknowledgementsAndCompleteDelivery()
        {
            using var certificates = new Certificates();
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var delivered = new TaskCompletionSource<MessageDeliveryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new GateEvents();
            events.NewMessageEvent += (channel, _, _, _, body, _, _) => { if (channel == "tls-server") received.TrySetResult(body); };
            events.MessagePartStatusEvent += part =>
            {
                if (part.ChannelName == "tls-client" && part.CommandStatus == 0)
                    Gate.Servers["tls-server"].SubmitDeliveryReport(part.ProviderMessageId, Common.MessageStatus.delivered_ACK_received);
            };
            events.MessageDeliveryStatusEvent += status => { if (status.IsComplete) delivered.TrySetResult(status); };
            var gate = new Gate(events);
            var server = gate.AddServerConnection("tls-server", "tls-user", "password");
            server.UseGsm7 = true;
            server.InboundMessageHandler = (message, _) => Task.FromResult(MessageAcknowledgement.Accept("stored-" + message.SequenceNumber));
            Client client = null;
            try
            {
                Server.StartAsync("127.0.0.1", 0, new SslServerAuthenticationOptions { ServerCertificate = certificates.Server });
                client = gate.AddClientConnection("tls-client", "127.0.0.1", ListenerPort(), "tls-user", "password");
                client.TlsOptions = certificates.ClientOptions();
                client.UseGsm7 = true;
                client.multypart_delay = 0;
                Assert.That(await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)), Is.True);
                WaitUntil(() => client.connected && server.connected, "TLS peers must finish SMPP binding");
                string body = new string('€', 81);
                Assert.That(client.SubmitSm(42, "1", "2", body, "auto"), Is.Zero);
                Assert.That(await received.Task.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(body));
                var result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                Assert.That(result.MessageId, Is.EqualTo(42));
                Assert.That(result.Status, Is.EqualTo(Common.MessageStatus.delivered_ACK_received));
                Assert.That(result.Parts.Count, Is.EqualTo(2));
                Assert.That(result.Parts[0].ProviderMessageId, Does.StartWith("stored-"));
            }
            finally { client?.Quit(); Server.Stop(); }
        }

        [TestCase("wrong-host.example", true)]
        [TestCase("localhost", false)]
        public async Task InvalidHostnameOrUntrustedCertificateCannotBind(string host, bool trust)
        {
            using var certificates = new Certificates();
            var gate = new Gate(new GateEvents());
            var server = gate.AddServerConnection("tls-invalid-server", "tls-user", "password");
            Client client = null;
            try
            {
                Server.StartAsync("127.0.0.1", 0, new SslServerAuthenticationOptions { ServerCertificate = certificates.Server });
                client = gate.AddClientConnection("tls-invalid-client", "127.0.0.1", ListenerPort(), "tls-user", "password");
                client.TlsOptions = certificates.ClientOptions(host, trust);
                client.reconnectDelay = 60000;
                Assert.That(await client.ConnectAsync().WaitAsync(TimeSpan.FromSeconds(5)), Is.False);
                Assert.That(client.connected, Is.False);
                Assert.That(server.connected, Is.False);
            }
            finally { client?.Quit(); Server.Stop(); }
        }

        [Test]
        public async Task QuitCancelsAStalledTlsHandshake()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new Gate(new GateEvents()).AddClientConnection("tls-cancel", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "user", "password");
            client.TlsOptions = new SslClientAuthenticationOptions { TargetHost = "localhost" };
            client.timeout = 60000;
            try
            {
                var connecting = client.ConnectAsync();
                using var peer = await listener.AcceptTcpClientAsync();
                var hello = new byte[1];
                await peer.GetStream().ReadExactlyAsync(hello).AsTask().WaitAsync(TimeSpan.FromSeconds(3));
                client.Quit();
                Assert.That(await connecting.WaitAsync(TimeSpan.FromSeconds(3)), Is.False);
            }
            finally { client.Quit(); }
        }

        [Test]
        public void MissingTlsHostnameFailsBeforeOpeningAConnection()
        {
            var client = new Gate(new GateEvents()).AddClientConnection("tls-no-host", "127.0.0.1", 1, "user", "password");
            try
            {
                client.TlsOptions = new SslClientAuthenticationOptions();
                Assert.Throws<InvalidOperationException>(() => client.ConnectAsync());
            }
            finally { client.Quit(); }
        }

        [Test]
        public void MissingServerCertificateFailsBeforeStartingTheListener()
        {
            Assert.Throws<ArgumentException>(() => Server.StartAsync("127.0.0.1", 0, new SslServerAuthenticationOptions()));
            Assert.That(Server.running, Is.False);
        }

        [Test]
        public async Task ClientDeadlineBoundsAStalledTlsHandshake()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new Gate(new GateEvents()).AddClientConnection("tls-timeout", "127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "user", "password");
            client.TlsOptions = new SslClientAuthenticationOptions { TargetHost = "localhost" };
            client.timeout = 250;
            client.reconnectDelay = 60000;
            try
            {
                var connecting = client.ConnectAsync();
                using var peer = await listener.AcceptTcpClientAsync();
                Assert.That(await connecting.WaitAsync(TimeSpan.FromSeconds(3)), Is.False);
                Assert.That(client.connected, Is.False);
            }
            finally { client.Quit(); }
        }

        [Test]
        public async Task StopCancelsPendingServerTlsHandshakes()
        {
            using var certificates = new Certificates();
            try
            {
                Server.StartAsync("127.0.0.1", 0, new SslServerAuthenticationOptions { ServerCertificate = certificates.Server });
                using var peer = new TcpClient();
                await peer.ConnectAsync(IPAddress.Loopback, ListenerPort());
                Server.Stop();
                var buffer = new byte[1];
                Assert.That(await peer.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(3)), Is.Zero);
            }
            finally { Server.Stop(); }
        }

        [Test]
        public async Task ServerHandshakeDeadlineAlsoBoundsTlsNegotiation()
        {
            using var certificates = new Certificates();
            using var pair = new SocketPair();
            var handshake = Server.CreateConnection(null, pair.Local, CancellationToken.None, 150,
                new SslServerAuthenticationOptions { ServerCertificate = certificates.Server });
            await handshake.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.That(pair.Remote.GetStream().ReadByte(), Is.EqualTo(-1));
        }
    }
}
