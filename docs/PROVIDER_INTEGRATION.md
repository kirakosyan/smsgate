# Provider integration (unreleased)

These APIs are available from this source branch and are not in the published SMSGate 1.0.0 package. Build with the SDK in `global.json`. The library remains dependency-free and targets .NET 10.

Run the complete loopback example with:

```bash
dotnet run --project examples/ProviderFeatures
```

It validates TLS against an ephemeral certificate, sends a two-part GSM-7 message, stores each received PDU in a temporary directory before acknowledging it, and waits for delivery of every segment. It does not modify the machine certificate store or contact an SMS provider. To test a locally packed version, restore with `-p:SMSGateVersion=<version> --source <package-directory>` and pass the same property to `dotnet run --no-restore`.

## GSM-7 and automatic encoding

```csharp
client.UseGsm7 = true; // Decode inbound data_coding 0 as the provider's GSM default alphabet.
client.SubmitSm(101, "MyBrand", "4712345678", "Price: €12 {offer}", "gsm7");
client.SubmitSm(102, "MyBrand", "4712345678", "Hello Привет 😀", "auto");
```

`gsm7` maps the GSM 03.38 default and extension alphabets to `data_coding = 0`. Extension characters `\f ^ { } \ [ ~ ] | €` consume two septets. Single messages hold up to 160 septets; concatenated parts hold up to `split_base_ascii` septets (153 by default). An escape sequence stays within its part. Truncation also respects escapes. Unsupported characters throw before sending.

`auto` on connection `SubmitSm` methods uses GSM-7 when every character is representable, otherwise the existing big-endian UTF-16 `unicode` format. Low-level request objects accept explicit formats such as `gsm7` and `unicode`; choose the format before calling their encoders. Configure `UseGsm7 = true` on low-level decoders before `Decode()` when reading a provider's GSM default alphabet.

The default `ascii` format and `UseGsm7 = false` retain the previous single-byte mapping. Match inbound decoding to the provider's documented default alphabet. Prefer unpacked septets (`use8bit = false`); packed SMSC interfaces lack a separate septet count, so a final CR can be ambiguous with GSM padding.

## TLS

```csharp
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

client.TlsOptions = new SslClientAuthenticationOptions
{
    TargetHost = "smpp.provider.example",
    CertificateRevocationCheckMode = X509RevocationMode.Online
};
await client.ConnectAsync();
```

`TargetHost` is required and is checked against the server certificate. It may differ from the TCP `host`, for example when connecting to a specific IP. Platform trust and hostname validation apply; TLS authentication failure does not fall back to TCP. A null `TlsOptions` retains plain TCP. The `timeout` deadline covers connection establishment and TLS authentication; `Quit()` cancels an outstanding handshake. TLS protocol selection follows the operating system unless explicitly configured in the authentication options.

For a TLS server, supply a certificate with its private key:

```csharp
using var certificate = X509Certificate2.CreateFromPemFile("server.crt", "server.key");
Server.StartAsync("0.0.0.0", 2776, new SslServerAuthenticationOptions
{
    ServerCertificate = certificate
});
```

The server authenticates TLS before reading SMPP credentials. TLS and bind share a ten-second deadline, and `Server.Stop()` cancels pending handshakes. Keep certificates and authentication options alive and unchanged while the listener or connection uses them. The .NET authentication options also support client certificates, certificate selection, and custom root trust policies. The loopback example uses a private trust policy for its temporary certificate; production deployments should use their provider's certificate chain and revocation policy.

## Per-segment submission and delivery

```csharp
eventsHub.MessagePartStatusEvent += part =>
{
    Console.WriteLine($"{part.ChannelName}/{part.MessageId}: part {part.PartNumber}/{part.PartCount}, " +
        $"provider={part.ProviderMessageId}, status={part.Status}, smpp={part.CommandStatus}");
};
eventsHub.DeliveryReceiptEvent += receipt =>
{
    Console.WriteLine($"Receipt {receipt.ProviderMessageId}: {receipt.Status}, " +
        $"error={receipt.ErrorCode}, network={receipt.NetworkErrorCode}");
};
eventsHub.MessageDeliveryStatusEvent += message =>
{
    if (message.IsComplete)
        Console.WriteLine($"{message.MessageId}: {message.Status} ({message.Parts.Count} parts)");
};
```

`MessagePartStatusEvent` exposes every segment's sequence number, response ID, raw SMPP `command_status`, and response/timeout outcome, even after another segment has failed. A null command status means no response arrived. A timeout is an unknown outcome: a provider may already have accepted the message. The library does not automatically retry it.

Delivery tracking is enabled for submissions requesting delivery reports. Standard `receipted_message_id` (0x001E), `message_state` (0x0427), and `network_error_code` (0x0423) TLVs are parsed on both `deliver_sm` and `data_sm`. TLVs take precedence over conflicting text fields; text receipts remain supported. `ErrorCode` contains the text `err` field; `NetworkErrorCode` contains all three TLV octets as uppercase hexadecimal. Invalid receipt TLV values are logged and ignored in favor of valid text fields; invalid TLV framing is rejected, and incomplete/unknown receipt IDs or states never imply delivery.

Every accepted segment is correlated by its opaque, case-sensitive provider ID. A message is delivered only when every part is delivered. A final part failure produces one terminal aggregate failure; later successes cannot overwrite it. Repeated and out-of-order receipts cannot move a final part back into an intermediate state. `ACCEPTD` remains intermediate by default so a later `DELIVRD` can complete delivery. Set `AcceptedReceiptsAreFinal = true` for providers that use terminal ACCEPTD. Successful `deliver_sm_resp` acknowledgements have no provider ID and are not delivery tracked.

Receipts can arrive before their submission response. The receipt event then has null `MessageId`, `PartNumber`, and `PartCount`; it is still emitted once, and cached internally for correlation when the response arrives. The subsequent aggregate event reflects it. Store per-part results and receipts if application-level persistence is required.

Tracking survives transient reconnects. `DeliveryTrackingLifetime` defaults to 24 hours and `MaxTrackedDeliveryParts` to 10,000; the same limit separately bounds unmatched early receipts and ambiguous IDs. Expiration or capacity eviction of unfinished tracked parts produces a terminal `timeout`, indicating that delivery is unknown. A shared provider ID across parts of one submission applies its receipt to all those parts. IDs reused across different submissions within the retention window are treated as ambiguous and produce a tracking error rather than guessed correlation. Explicit `Quit()` expires unfinished tracking owned by the quitting connection and clears its default private store. Externally assigned shared stores retain unmatched receipts and ambiguous IDs for other connections until expiry or an explicit store `Clear()`. State is held in memory and does not survive process restarts. Choose retention and capacity for the expected receipt latency and traffic volume.

Tracking uses a connection-local store by default. For separate transmitter and receiver binds, assign one `DeliveryTrackingStore` to both clients before connecting; the [service integration guide](SERVICE_INTEGRATION.md) covers provider scoping and ownership.

The existing `MessageStatusEvent` still reports aggregate submission success using the first segment's ID. The existing `MessageDeliveryReportEvent` still emits provider receipt ID/status pairs. Richer tracking is available through the new events without changing those signatures.

## Storage before acknowledgement

```csharp
server.AcknowledgementTimeout = 5000;
server.MaxPendingAcknowledgements = 100;
server.InboundMessageHandler = async (message, cancellationToken) =>
{
    // Commit each PDU to your database or durable queue before returning.
    await StorePduAsync(message, cancellationToken);
    return MessageAcknowledgement.Accept(message.MessageId);
    // Or return MessageAcknowledgement.Reject(0x58) when storage is overloaded.
};
```

The handler runs outside protocol locks for `submit_sm`, `deliver_sm`, and `data_sm`, including delivery receipts. It receives the sequence number, decoded body, multipart metadata, optional parameter values as hexadecimal, and parsed receipt fields when present. It runs once per PDU, so persist each multipart segment independently. Segments and concurrent messages can be processed concurrently; responses are correlated by sequence number.

Returning `Accept()` uses the generated `message.MessageId`; passing an ID lets the application choose its own. IDs must contain at most 64 ASCII characters without NUL. Use a unique ID for each accepted `submit_sm` or `data_sm` PDU so segment receipts can be correlated. The `deliver_sm_resp` wire ID remains empty as required by SMPP; the chosen application ID is available in `NewMessageEvent`.

`Reject(nonzeroStatus)` returns that SMPP error and does not publish an accepted message or delivery receipt. Exceptions, invalid response IDs, and acknowledgement deadline expiration return `ESME_RSYSERR` (0x08). Excess pending work receives `ESME_RTHROTTLED` (0x58) without invoking the handler. The default acknowledgement deadline is 10,000 milliseconds. Keep it below the peer's response deadline.

Keepalives and protocol reads continue while a handler awaits storage. Disconnects cancel the handler token and suppress its response; a late handler cannot acknowledge a replacement session. Handlers must honor cancellation. A timed-out handler that continues running retains a concurrency slot until it exits, preventing repeated timeouts from creating unlimited active handlers.

`NewMessageEvent` is emitted after acceptance, once all multipart segments are accepted and assembled. Rejected segments do not enter reassembly. That event and other ordinary events remain observational: exceptions from them do not retract an acknowledgement. With no `InboundMessageHandler`, the original automatic acknowledgement behavior remains.

Storage can succeed while an acknowledgement is lost. Applications must choose their own duplicate detection and retry policy; this API does not provide exactly-once delivery or a built-in durable store.

See the [service integration guide](SERVICE_INTEGRATION.md) for bounded asynchronous submissions, validated options, dependency injection, graceful shutdown, metrics, and independent wire validation.

## References

- [SMPP 3.4 specification](https://smpp.org/SMPP_v3_4_Issue1_2.pdf), including receipt TLVs and message states.
- [Sinch SMPP integration](https://developers.sinch.com/docs/sms/smppold), including GSM alphabets and TLS endpoints.
- [.NET SslStream authentication](https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslstream.authenticateasclientasync?view=net-10.0).
