# Running SMSGate in a service

These APIs are unreleased. They target .NET 10 and add no dependencies to the core package. The existing `Gate`, `Client`, `Server`, synchronous submissions, and event signatures remain available.

## An instance-owned client

```csharp
var eventsHub = new GateEvents();
await using var gateway = new SmppGateway(eventsHub);
var client = gateway.AddClient(new SmppClientOptions
{
    ChannelName = "provider",
    Host = "smpp.provider.example",
    Port = 2775, // Use the port supplied by your provider.
    SystemId = systemId,
    Password = password,
    TlsOptions = new SslClientAuthenticationOptions
    {
        TargetHost = "smpp.provider.example"
    },
    UseGsm7 = true, // Enable only when DCS 0 means GSM-7 on this account.
    SourceTon = 1, SourceNpi = 1,
    DestinationTon = 1, DestinationNpi = 1,
    Submission = new()
    {
        QueueCapacity = 1000,
        MaxInFlightPdus = 10,
        PdusPerSecond = 20,
        MaxThrottleRetries = 1,
        ThrottleRetryDelay = TimeSpan.FromSeconds(1)
    }
});

BindResult bind = await client.ConnectAndBindAsync(cancellationToken);
if (!bind.Bound)
    throw new InvalidOperationException($"Bind failed: {bind.CommandStatus}");

SubmissionResult result = await client.SubmitAsync(
    new OutboundMessage(42, "1000", "2000", "Hello €"), cancellationToken);
foreach (var part in result.Parts)
    Console.WriteLine($"Part {part.PartNumber}: {part.ProviderMessageId}, SMPP {part.CommandStatus}");

await gateway.StopAsync(shutdownToken);
```

Import `Smpp`, `Smpp.Events`, and `System.Net.Security`. Load credentials from your service's configuration/secret provider. `SmppClientOptions.Validate()` rejects invalid ports, directions, credential lengths, octet fields, TLS host settings, and submission limits before creating a client. Credentials are excluded from its string representation. Treat TLS options and legacy public connection fields as startup configuration; do not mutate them during traffic.

`SmppGateway` owns its clients and permits the same channel names in separate gateway instances. Its clients do not enter `Gate.Clients`. `SmppClientOptions.CreateClient(events)` also creates an independently owned client; dispose it yourself. Automatic reconnect works for both forms. The legacy static server listener and `Gate` registries remain available for existing applications.

`ConnectAndBindAsync` waits for a bind response and exposes the raw rejection status. A null status means no bind response was obtained. Cancellation cancels that caller's wait; concurrent callers share the transport attempt. Use `Quit`, `StopAsync`, or disposal to stop the underlying client permanently.

## Bounded submissions and safe retries

`SubmitAsync` is the queue-based API. Its limits count actual submitted PDUs, including multipart segments. Keepalives and responses use the protocol path independently of this queue. `MaxInFlightPdus` bounds outstanding asynchronous submission PDUs; `PdusPerSecond = 0` disables pacing. Limits are fixed after the queue starts. At most `QueueCapacity` messages wait in the queue and at most `MaxInFlightPdus` message jobs run. A single admitted message may have up to 255 segments. Cancelled queued entries occupy their slot until the worker consumes them.

Queue saturation returns `QueueFull` immediately. A disconnected client returns `NotConnected`; it does not silently retain messages until reconnection. `Accepted` means every PDU received a successful submission response. Use `MessageDeliveryStatusEvent` for handset delivery. `Rejected` exposes all raw per-part statuses, including vendor errors. `Failed` identifies a local error before any PDU was written; `ErrorMessage` describes the failure and `MayHaveBeenAccepted` is false. Exceptions after a write remain `Unknown` and preserve that uncertainty. Null command statuses mean no acknowledgement arrived. `Unknown`, or cancellation after a write, may mean the provider accepted the message; inspect `MayHaveBeenAccepted`. Cancellation cannot retract a written PDU. Late responses still release the in-flight slot and update delivery tracking.

Retries are disabled by default. When enabled, a retry occurs only if **every PDU in the attempt explicitly returned `ESME_RTHROTTLED` (0x58)**. The number of retries is bounded, and the delay increases by the configured delay for each retry. A partial acceptance, transport loss, timeout, or any other error is never automatically retried. `Attempts` reports the count; `Parts` describes the last attempt. Per-PDU `MessagePartStatusEvent` notifications expose every attempt and its raw status, identified by sequence number. Legacy `MessageStatusEvent` and aggregate delivery events report the final retry outcome; a retryable throttled attempt does not publish a terminal rejection. Cancellation publishes a terminal legacy `timeout`, since the legacy status enum has no cancellation value.

The legacy `SubmitSm` method continues to send directly and return its original integer status. It bypasses the asynchronous queue's limits. Use `SubmitAsync` consistently on a provider connection when its rate/window limits must apply to every application submission. Keep a durable application outbox and a provider-specific duplicate policy when persistence or retries across process restarts are required.

## Separate transmitter and receiver binds

Create one `DeliveryTrackingStore` per provider account and assign the same instance to its TX and RX clients **before connecting**. Keep provider accounts isolated even if their message IDs appear globally unique.

```csharp
var tracking = new DeliveryTrackingStore
{
    Lifetime = TimeSpan.FromHours(48),
    Capacity = 50000
};
// In both clients' SmppClientOptions:
// DeliveryStore = tracking
// TX: Transmit = true, Receive = false, ChannelName = "provider-tx"
// RX: Transmit = false, Receive = true, ChannelName = "provider-rx"
```

Receipt events identify the receiving channel. Correlated aggregate delivery events identify the submitting channel and application ID. Receipts arriving before the submit response are retained by the shared store. Closing an RX connection leaves the TX client's tracked deliveries intact; quitting a submitting client expires that client's unfinished deliveries. `tracking.Clear()` explicitly expires all unfinished tracking in that store. Lifetime and capacity settings on a connection forward to its store and therefore affect every connection sharing it. `AcceptedReceiptsAreFinal` is the submitting connection's provider policy, including receipts received before correlation. Early ACCEPTD and terminal alternatives are retained until that policy is known; a late ENROUTE cannot erase acceptance. Assign a store before configuring `DeliveryTrackingLifetime` or `MaxTrackedDeliveryParts`; replacing a store with non-default retention settings throws instead of discarding those settings. Quitting a default private store clears all of its caches; externally assigned stores retain unmatched and ambiguous IDs for their other users until expiry or explicit `Clear()`.

The store is bounded and lives in memory. It supports connections within one process; use application persistence and coordination when receipts can arrive in a different process or after a restart.

## Dependency injection and shutdown

The [WorkerService example](../examples/WorkerService/Program.cs) uses the .NET Generic Host:

```csharp
builder.Services.AddSingleton<GateEvents>();
builder.Services.AddSingleton<SmppGateway>();
builder.Services.AddHostedService<YourWorker>();
```

Inject the gateway into your worker, create its clients at startup, and call `gateway.StopAsync(stoppingToken)` from your service shutdown path. The example uses a framework reference for Generic Host/DI; the SMSGate package itself remains dependency-free.

`StopAsync` rejects new asynchronous work, drains admitted submissions and inbound acceptance handlers, then sends unbind and closes the transport. Supply a shutdown deadline; cancellation forces cleanup and propagates cancellation to the caller. `Quit` and synchronous disposal stop immediately. Asynchronous connection disposal allows up to its response timeout to drain; gateway disposal allows ten seconds for its clients. An application acceptance handler must honor cancellation. Notification subscribers run independently, and application storage must be committed inside `InboundMessageHandler` before acceptance.

Run the local service example with:

```sh
dotnet run --project examples/WorkerService --configuration Release
```

## Metrics and structured events

`client.StateChanged` reports connecting, transport connected, bound, reconnecting, and stopped states. `client.Diagnostic` exposes named protocol events with sequence numbers and raw response statuses, without credentials, addresses, or message bodies. These callbacks use the same isolated notification dispatch as ordinary events. Sends capture the submitting caller's trace context independently; the persistent worker does not retain caller AsyncLocal values or logging scopes.

The `SMSGate` meter publishes:

| Instrument | Meaning |
| --- | --- |
| `smpp.queue.depth` | Waiting asynchronous submissions, as increments/decrements |
| `smpp.pdus.in_flight` | Outstanding asynchronous submission PDUs |
| `smpp.pdus.sent` | Asynchronous submission PDUs written |
| `smpp.responses` | Submission responses/unknown outcomes, with a status tag |
| `smpp.ack.duration` | Time from writing to a response/timeout, milliseconds |
| `smpp.delivery.receipts` | Received receipts, with a message-state tag |
| `smpp.delivery.completed` | Terminal aggregate deliveries, with a status tag |
| `smpp.delivery.duration` | Time from submission acknowledgement to a final part receipt, milliseconds |

Measurements are queued on the notification dispatcher, so synchronous meter listeners run outside protocol and delivery-store locks. A slow listener can delay other notifications on that connection, while protocol reads and keepalives continue. Counters aggregate across connections in the process. Use `MeterListener` or configure an OpenTelemetry exporter for meter `SMSGate`; `SmppDiagnostics.ActivitySource` emits `smpp.submit` activities with channel and outcome tags. Structured channel events provide per-connection attribution without adding channel or message IDs to metric labels.

## Provider configuration recipes

- For numeric international addresses, start with TON 1/NPI 1 and the provider's required number format. For an alphanumeric source, providers commonly require source TON 5/NPI 0. Confirm the exact source policy and whether `+` is allowed with the provider.
- Confirm that `data_coding = 0` means GSM-7 on that account. `UseGsm7` defaults to false in both legacy connections and validated client options; enable it explicitly to configure inbound GSM-7 decoding; `BodyFormat = "auto"` selects GSM-7 or Unicode outbound. Packed septets are a separate provider option; leave `use8bit` false unless the provider explicitly requires packed transport. The legacy packed mode uses a lengthless CR-padding convention and cannot disambiguate every trailing CR case from peers using a different padding rule.
- Request delivery receipts and verify text versus standard receipt TLVs, shared versus per-segment IDs, ACCEPTD semantics, and whether receipts arrive on a separate receiver bind.
- Use the provider's TLS hostname and port with platform certificate validation. Match its supported bind type, TPS allowance, PDU window, response deadline, keepalive, and reconnect delay. Avoid blind retries on unknown outcomes.
- Run the [provider feature example](../examples/ProviderFeatures/Program.cs) for TLS and durable per-PDU acknowledgement, and adapt it to a provider test account before production.

## Independent wire validation

`scripts/interop.py` implements a small SMPP peer using Python's standard library. It shares no protocol codec with SMSGate. Both client and server paths check bind, GSM-7 bytes, submission response IDs, receipt TLVs, keepalive/unbind behavior, and an explicit throttle/retry exchange; the server path also checks unknown-command handling.

```sh
dotnet build examples/InteropPeer --configuration Release -warnaserror
python scripts/interop.py examples/InteropPeer/bin/Release/net10.0/InteropPeer.dll
```

CI runs this exchange against the built NuGet package on Windows and Linux, alongside the TLS and hosted-service examples. This is a protocol subset check, not qualification against a live SMSC or every SMPP implementation.
