using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Smpp;
using Smpp.Events;

// Loopback demonstration only: trust one ephemeral certificate without changing the machine trust store.
using var key = RSA.Create(2048);
var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var names = new SubjectAlternativeNameBuilder();
names.AddDnsName("localhost");
request.CertificateExtensions.Add(names.Build());
request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyCertSign, true));
using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddDays(1));
using var certificate = X509CertificateLoader.LoadPkcs12(generated.Export(X509ContentType.Pfx), null);
var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust, RevocationMode = X509RevocationMode.NoCheck };
policy.CustomTrustStore.Add(certificate);

// Reserve an available loopback port. A different process could claim it before StartAsync; startup would then fail.
int port;
using (var reservation = new TcpListener(IPAddress.Loopback, 0))
{
    reservation.Start();
    port = ((IPEndPoint)reservation.LocalEndpoint).Port;
}

string store = Path.Combine(Path.GetTempPath(), "smsgate-provider-example-" + Guid.NewGuid().ToString("n"));
Directory.CreateDirectory(store);
var deadline = TimeSpan.FromSeconds(10);
var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
var delivered = new TaskCompletionSource<MessageDeliveryStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
var events = new GateEvents();
var gate = new Gate(events);
var server = gate.AddServerConnection("provider-demo", "demo", "demo-password");
server.UseGsm7 = true;
server.InboundMessageHandler = async (message, token) =>
{
    // Each multipart segment has its own stored ID. A real service would commit a database transaction here.
    string id = message.MessageId;
    await using var file = new FileStream(Path.Combine(store, id + ".json"), FileMode.CreateNew,
        FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough);
    await JsonSerializer.SerializeAsync(file, message, cancellationToken: token);
    await file.FlushAsync(token);
    file.Flush(flushToDisk: true);
    return MessageAcknowledgement.Accept(id);
};
events.NewMessageEvent += (channel, _, _, _, body, _, _) =>
{
    if (channel == "provider-demo") received.TrySetResult(body);
};
events.MessagePartStatusEvent += part =>
{
    if (part.ChannelName == "client-demo" && part.CommandStatus == 0)
    {
        Console.WriteLine($"Part {part.PartNumber}/{part.PartCount} accepted as {part.ProviderMessageId}");
        // Simulate handset delivery only after the server has stored this segment.
        server.SubmitDeliveryReport(part.ProviderMessageId, Common.MessageStatus.delivered_ACK_received);
    }
};
events.MessageDeliveryStatusEvent += status =>
{
    if (status.ChannelName == "client-demo" && status.IsComplete) delivered.TrySetResult(status);
};
Client? client = null;
try
{
    Server.StartAsync("127.0.0.1", port, new SslServerAuthenticationOptions { ServerCertificate = certificate });
    client = gate.AddClientConnection("client-demo", "127.0.0.1", port, "demo", "demo-password");
    client.TlsOptions = new SslClientAuthenticationOptions { TargetHost = "localhost", CertificateChainPolicy = policy };
    client.UseGsm7 = true;
    client.multypart_delay = 0;
    if (!await client.ConnectAsync() || !SpinWait.SpinUntil(() => client.connected && server.connected, deadline))
        throw new InvalidOperationException("The TLS SMPP session did not bind.");
    string body = new string('€', 81); // 162 septets, requiring two segments.
    if (client.SubmitSm(42, "15550001", "15550002", body, "auto") != 0)
        throw new InvalidOperationException("Submission failed.");
    if (await received.Task.WaitAsync(deadline) != body)
        throw new InvalidOperationException("The reassembled GSM-7 message did not match.");
    var result = await delivered.Task.WaitAsync(deadline);
    if (result.Status != Common.MessageStatus.delivered_ACK_received || result.Parts.Count != 2)
        throw new InvalidOperationException("Every segment must be delivered.");
    Console.WriteLine($"Provider features passed: validated TLS, GSM-7, stored acknowledgements, and {result.Parts.Count} delivered parts.");
    Console.WriteLine($"Stored PDUs: {store}");
}
finally
{
    client?.Quit();
    Server.Stop();
}
