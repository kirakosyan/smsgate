# SMSGate - SMPP client and server for .NET

![SMSGate - SMPP client and server for .NET](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/smsgate-logo.png)

[![CI](https://img.shields.io/github/actions/workflow/status/kirakosyan/smsgate/ci.yml?branch=master&style=for-the-badge&label=CI)](https://github.com/kirakosyan/smsgate/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SMSGate.svg?style=for-the-badge)](https://www.nuget.org/packages/SMSGate)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg?style=for-the-badge)](#supported-platforms)
[![.NET Framework 4.7.2+](https://img.shields.io/badge/.NET_Framework-4.7.2%2B-512BD4.svg?style=for-the-badge)](#supported-platforms)
[![MIT license](https://img.shields.io/badge/license-MIT-blue.svg?style=for-the-badge)](https://github.com/kirakosyan/smsgate/blob/master/LICENSE)
[![Sponsor on Patreon](https://img.shields.io/badge/Sponsor-Patreon-FF424D.svg?style=for-the-badge&logo=patreon&logoColor=white)](https://patreon.com/ArmenKirakosyan?utm_medium=unknown&utm_source=join_link&utm_campaign=creatorshare_creator&utm_content=copyLink)

[![Windows: covered by .NET 10 CI](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/windows-ci.svg)](#supported-platforms)
[![Linux: covered by .NET 10 CI](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/linux-ci.svg)](#supported-platforms)
[![macOS: not yet validated](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/macos-unverified.svg)](#supported-platforms)

SMSGate is a lightweight **SMPP 3.4 client and server library for .NET 10 and .NET Framework 4.7.2+**. Build SMS services with asynchronous submissions, delivery tracking, and control over incoming message acknowledgements. The .NET 10 target has no package dependencies; Framework uses Microsoft compatibility packages.

**Source status:** this branch prepares **1.2.0**, including Framework support. The published **1.1.1** package supports .NET 10 only. Until 1.2.0 is released, build and pack this source to use the Framework target.

- **Send and receive:** client and server sessions, multipart SMS, GSM-7, Unicode, binary messages, and WAP Push.
- **Track delivery:** per-segment provider IDs, text and TLV receipts, and aggregate multipart delivery status.
- **Control traffic:** bounded async queues, PDU rate and window limits, and explicit submission outcomes.
- **Operate services:** optional TLS, storage before acknowledgement, graceful shutdown, structured diagnostics, and metrics.

[Quick start](#quick-start) · [Supported platforms](#supported-platforms) · [Service integration](https://github.com/kirakosyan/smsgate/blob/master/docs/SERVICE_INTEGRATION.md) · [Provider integration](https://github.com/kirakosyan/smsgate/blob/master/docs/PROVIDER_INTEGRATION.md) · [Changelog](https://github.com/kirakosyan/smsgate/blob/master/CHANGELOG.md)

## Install

```bash
dotnet add package SMSGate --version 1.2.0
```

The NuGet package is named `SMSGate`; the assembly and namespaces are `Smpp`. Version 1.2.0 contains `net10.0` and `net472` assemblies; NuGet selects the appropriate target. For Framework applications, use PackageReference and enable automatic binding redirects. See the [Framework guide](https://github.com/kirakosyan/smsgate/blob/master/docs/FRAMEWORK_SUPPORT.md) for project setup, TLS options, and runtime validation.

## Supported platforms

| Operating system | .NET 10 (`net10.0`) | .NET Framework 4.7.2+ (`net472`) | Validation |
| --- | --- | --- | --- |
| ![Windows](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/windows.svg) Windows | Supported | Available in 1.2.0 | Debug/Release tests and package examples; Framework CI uses the installed newer runtime |
| ![Linux](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/linux.svg) Linux | Supported | Windows only | Debug/Release .NET 10 tests and package examples in Ubuntu CI |
| ![macOS](https://raw.githubusercontent.com/kirakosyan/smsgate/master/assets/branding/platforms/macos.svg) macOS | Not yet qualified | Windows only | No macOS CI lane |

Framework 4.8 and 4.8.1 applications consume the `net472` assembly. Minimum-runtime validation is recorded separately in the [Framework guide](https://github.com/kirakosyan/smsgate/blob/master/docs/FRAMEWORK_SUPPORT.md); a passing test on 4.8.1 is not evidence of execution on 4.7.2. CI does not include dedicated ARM64 qualification. Use an operating system supported by your selected runtime. The package contains no native libraries; Mono, browser, and mobile runtimes are not qualified.

## Quick start

Create a .NET 10 console application, install the package, and set `SMPP_HOST`, `SMPP_PORT`, `SMPP_SYSTEM_ID`, `SMPP_PASSWORD`, `SMPP_SENDER`, and `SMPP_RECIPIENT` for your provider account. The same async APIs work on Framework; put the code inside an `async Task Main` method and use C# 9 or later for `await using`. The example connects over TCP, waits for a successful bind, submits a message through the bounded async queue, and prints each segment's provider response.

```csharp
using System;
using System.Threading;
using Smpp;
using Smpp.Events;

static string Setting(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Set {name} before running.");

var eventsHub = new GateEvents();
await using var gateway = new SmppGateway(eventsHub);
var client = gateway.AddClient(new SmppClientOptions
{
    ChannelName = "provider",
    Host = Setting("SMPP_HOST"),
    Port = int.Parse(Setting("SMPP_PORT")),
    SystemId = Setting("SMPP_SYSTEM_ID"),
    Password = Setting("SMPP_PASSWORD"),
    Submission = new SubmissionOptions
    {
        QueueCapacity = 100,
        MaxInFlightPdus = 5,
        PdusPerSecond = 20
    }
});

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var bind = await client.ConnectAndBindAsync(deadline.Token);
if (!bind.Bound)
    throw new InvalidOperationException($"SMPP bind failed: {bind.CommandStatus}");

var result = await client.SubmitAsync(new OutboundMessage(
    MessageId: 42,
    Sender: Setting("SMPP_SENDER"),
    Recipient: Setting("SMPP_RECIPIENT"),
    Body: "Hello from SMSGate"), deadline.Token);

Console.WriteLine($"Submission: {result.Outcome}");
if (!result.Accepted)
    Console.WriteLine($"May have been accepted: {result.MayHaveBeenAccepted}");
foreach (var part in result.Parts)
    Console.WriteLine($"Part {part.PartNumber}: {part.ProviderMessageId}, SMPP {part.CommandStatus}");
```

`Accepted` confirms successful provider submission. Handset delivery is reported separately through `MessageDeliveryStatusEvent`; keep your service connected to receive receipts. An unknown outcome may already have been accepted by the provider, so check `MayHaveBeenAccepted` before deciding how to retry. `await using` drains pending work and closes the gateway when the example ends.

For a TLS endpoint, configure `TlsOptions` with the provider's certificate hostname and port. Match the provider's TON/NPI, GSM-7, rate, and window settings using the [provider guide](https://github.com/kirakosyan/smsgate/blob/master/docs/PROVIDER_INTEGRATION.md). Credentials belong in your application's configuration or secret provider.

### Runnable examples

These examples use a local SMPP peer and need no provider account:

| Example | Demonstrates |
| --- | --- |
| [WorkerService](https://github.com/kirakosyan/smsgate/tree/master/examples/WorkerService) | `SmppGateway`, `SubmitAsync`, Generic Host/DI, delivery tracking, and graceful shutdown |
| [ProviderFeatures](https://github.com/kirakosyan/smsgate/tree/master/examples/ProviderFeatures) | Validated TLS, GSM-7 multipart messages, and storage before acknowledgement |
| [LocalExchange](https://github.com/kirakosyan/smsgate/tree/master/examples/LocalExchange) | A complete client/server exchange using the legacy `Gate` API |
| [FrameworkExchange](https://github.com/kirakosyan/smsgate/tree/master/examples/FrameworkExchange) | A `net472` package consumer using `SmppGateway`, multipart `SubmitAsync`, delivery tracking, and graceful shutdown |
| [InteropPeer](https://github.com/kirakosyan/smsgate/tree/master/examples/InteropPeer) | Independent Python SMPP wire exchanges in both directions |

```bash
dotnet run --project examples/WorkerService --configuration Release
```

Run that command from a clone of this repository. See the [service integration guide](https://github.com/kirakosyan/smsgate/blob/master/docs/SERVICE_INTEGRATION.md) for lifecycle, delivery tracking, diagnostics, and persistence guidance.

### Upgrading from 1.0.0

SMSGate **1.1.0** adds opt-in GSM-7 encoding, complete multipart delivery tracking, TLS, and application-controlled acknowledgements. Existing APIs, event signatures, plain TCP, automatic acknowledgements, and inbound alphabet defaults remain available. Enable `UseGsm7` only when your provider uses the GSM default alphabet for DCS 0. Configure TLS and an `InboundMessageHandler` explicitly when required. Legacy `SubmitSm` bypasses the new queue limits; use `SubmitAsync` consistently when those limits must apply to all submissions. Queues and delivery tracking are held in memory, so keep a durable application outbox and duplicate policy when recovery across process restarts is required.

## API overview

- `SmppGateway` owns client connections and provides graceful shutdown for services and dependency injection.
- `SmppClientOptions` validates provider configuration before creating a client.
- `Client.ConnectAndBindAsync()` waits for the bind response; `Client.SubmitAsync()` queues submissions and returns per-PDU outcomes.
- `Gate` is the legacy entry point used to register connections and share a single `GateEvents` instance.
- `Server` registers accepted credentials and hosts a static TCP listener that authenticates inbound binds.

## Connection Model

### Client

Create an instance-owned client with `SmppGateway.AddClient(...)` and await `ConnectAndBindAsync()` before calling `SubmitAsync()`. The returned `BindResult.Bound` confirms the provider accepted the bind.

The legacy `Gate.AddClientConnection(...)`, `Connect()`, and `ConnectAsync()` APIs remain available. A true return value from `Connect()` or `ConnectAsync()` means the TCP connection opened and the bind request was sent. Wait for `connected == true` before submitting messages through that API; it becomes true only after the matching successful bind response. Concurrent connect calls share one attempt. Connection attempts run outside the session lock, and `Quit()` cancels an in-progress attempt.

EOF, socket errors, rejected binds, invalid frame lengths, and unanswered bind or keepalive requests trigger a reconnect after `reconnectDelay`. Only one retry is scheduled per connection. A late message acknowledgement expires that submission without disconnecting or timing out other messages. `Quit()` cancels retries, closes the session, and unregisters the client permanently.

Binding mode is selected from these flags before calling `Connect()`:

- `direction_in = true`, `direction_out = true` -> bind as transceiver
- `direction_in = false`, `direction_out = true` -> bind as transmitter
- `direction_in = true`, `direction_out = false` -> bind as receiver

### Server

Create one or more logical server channels with `Gate.AddServerConnection(...)`, then start the shared listener with `Server.StartAsync(ip, port)`.

Each registered server channel acts as an allowed credential pair. During bind, the listener authenticates the inbound `system_id` and `password` against the configured server channels and routes the accepted session to the matching server object. Remote unbind, EOF, and timeout close the session while preserving its credential registration for a later bind. Explicit `Quit()` or `Server.Stop()` unregisters it.

`StartAsync` starts the listener before returning; startup errors are thrown to the caller. A second start while running is rejected. Each channel accepts one active session at a time, and a concurrent bind is rejected without replacing the existing socket. Transient accept errors retry with a short backoff. Unauthenticated binds use asynchronous reads with one ten-second deadline for the whole handshake; `Stop()` cancels pending handshakes.

## Sending Messages

For client services, use `Client.SubmitAsync(...)` as shown in the quick start. It applies the configured queue, rate, and window limits and returns a `SubmissionResult` with every segment's response. See the [service guide](https://github.com/kirakosyan/smsgate/blob/master/docs/SERVICE_INTEGRATION.md) for outcome handling and retry rules.

Both `Client` and `Server` also expose the legacy direct-send `SubmitSm(...)` API below. It bypasses the async queue's limits.

```csharp
int result = client.SubmitSm(
	message_id: 101,
	sender: "15550001",
	recipient: "15550002",
	body: "Test message",
	body_format: "ascii",
	delivery_report: true);
```

Return codes:

- `0` - accepted for sending
- `1` - no active connection
- `2` - throttled by the configured send limit
- `3` - sequence reservation failed (occupied ranges are normally skipped automatically)
- `502` - transport write failed

For server-originated traffic:

- `use_deliver_sm = false` sends `data_sm`
- `use_deliver_sm = true` sends `deliver_sm`

## Body Formats

The library maps these body format names internally:

- `ascii` - text using the library's existing single-byte mapping
- `gsm7` - the GSM 03.38 default alphabet and extension table, sent with `data_coding = 0`
- `auto` - on `Client.SubmitSm` and `Server.SubmitSm`, selects GSM-7 when possible, otherwise Unicode
- `binary` - hexadecimal octets, such as `00FF80`; decoded bodies are uppercase hex
- `latin` - ISO-8859-1 text
- `unicode` - big-endian UTF-16 text
- `wap_push` - a URL or an existing WAP Push hexadecimal payload

For `submit_sm` and `deliver_sm`, `large_message` selects multipart (1), a `message_payload` TLV (2), truncation (3), or an exception when the single-message limit is exceeded (4). Single-message limits are 160 ASCII characters, 70 UTF-16 code units, or 140 Latin/binary octets. Multipart bodies use up to 153 ASCII characters, 63 UTF-16 code units, or 134 Latin/binary octets per segment. Surrogate pairs stay together. More than 255 segments or a payload larger than 65,535 octets is rejected before sending. `data_sm` always uses a payload TLV.

Long WAP Push bodies must use payload mode (`large_message = 2`); multipart mode rejects them before sending so that the port-addressing UDH remains valid.

For GSM-7 providers, set `UseGsm7 = true` on the receiving connection and send with `body_format: "gsm7"` or `"auto"`. Extension characters such as `€`, `{`, and `^` count as two septets; multipart splitting keeps escape sequences together. Explicit `gsm7` rejects unsupported characters instead of replacing them. Existing `ascii` behavior remains available for compatibility.

Multipart reassembly supports `submit_sm`, `deliver_sm`, and `data_sm` and separates sender, recipient, format, and command type as well as the reference number. Already-acknowledged segments survive transient disconnects and rebinds. Duplicate segments do not count twice; incomplete assemblies expire after five minutes, and explicit `Quit()` clears them. Reusing the same reference for overlapping messages with identical addressing and format remains ambiguous on the wire.

## Key Configuration Fields

For new clients, start with validated `SmppClientOptions` and `SubmissionOptions`. The fields below remain available on `Client` or `Server` for legacy configuration; set them before traffic begins.

| Field | Default | Purpose |
| --- | --- | --- |
| `debug` | `false` | Includes raw PDU payloads in `ChannelEvent` logs; outgoing bind credentials are omitted |
| `system_type` | `""` | SMPP system type |
| `source_ton` / `source_npi` | `0` / `0` | Source addressing configuration |
| `destination_ton` / `destination_npi` | `0` / `0` | Destination addressing configuration |
| `registered_delivery` | `1` | Requests delivery reporting when supported |
| `large_message` | `1` | Large-message handling: multipart, payload, truncate, or fail |
| `use_deliver_sm` | `false` | Makes server-originated sends use `deliver_sm` instead of `data_sm` |
| `use8bit` | `false` | Packs ASCII septets into octets; configure both sending and receiving peers consistently |
| `split_base_ascii` | `153` | ASCII multipart split threshold |
| `multypart_delay` | `100` | Delay in milliseconds between multipart segments |
| `reconnectDelay` | `10000` | Delay before reconnect attempts in most reconnect paths |
| `keep_alive` | `60000` | Enquire-link interval in milliseconds; must be positive |
| `timeout` | `10000` | Bind, command, and message-ACK timeout in milliseconds; must be positive |

## Events

`GateEvents` is the main integration surface for application code.

Connection events are dispatched asynchronously in order per connection, outside the session lock. A handler can call connection methods or wait for another thread to do so without blocking protocol reads or timers. Exceptions from handlers are logged and later events continue. Different connections can invoke the shared event hub concurrently; application handlers should synchronize shared state. Method return does not imply that its event handlers have finished.

| Event | Purpose |
| --- | --- |
| `Event` | General informational and error messages |
| `ChannelEvent` | Low-level SMPP channel activity, including bind, send, receive, and disconnect messages |
| `MessageStatusEvent` | Message lifecycle changes for submitted messages |
| `MessageDeliveryReportEvent` | Delivery reports parsed from inbound `deliver_sm` PDUs |
| `NewMessageEvent` | Application-facing inbound messages with sender, recipient, body, and format |
| `MessagePartStatusEvent` | Every PDU acknowledgement or timeout, with segment number, provider ID, and raw SMPP response code |
| `DeliveryReceiptEvent` | Parsed receipt fields, error details, and message/segment correlation when available |
| `MessageDeliveryStatusEvent` | Multipart delivery progress and one terminal result with a snapshot of all parts |

Each submission emits its initial waiting/routed status once. A multipart submission reports `sent` only after all segments succeed, using the first segment's SMSC message ID. The first nonzero response reports `rejected`, including unrecognized vendor errors; later successes or missing acknowledgements cannot replace that rejection. Delivery receipts require a valid ID and recognized status. Missing, truncated, or unknown receipt fields are logged instead of being treated as successful delivery.

The legacy events retain these semantics. The new tracking events expose every segment ID and support standard receipt TLVs on both `deliver_sm` and `data_sm`, with text receipts as a fallback. A complete message is reported delivered only after every segment is delivered. See the integration guide for tracking retention limits, early receipts, and acknowledgement control.

Complete message requests that cannot be decoded receive an error `generic_nack` with their sequence number. Invalid frame lengths close the stream because its boundaries cannot be recovered. `alert_notification` and `outbind` on an established session receive no response.

Useful message states come from `Common.MessageStatus`, including `submitted_waiting_for_ACK`, `sent`, `delivered_ACK_received`, `queued`, `timeout`, `rejected`, and `message_undeliverable`.

## Tested Scenarios

The test suite includes the original command and integration tests plus regression fixtures for:

- every byte split position in representative PDUs, coalesced frames, malformed trailing frames, and maximum payloads
- ASCII, Latin, Unicode, binary, packed ASCII, and WAP Push encoding, including length boundaries and wire-level length/UDH assertions
- multipart reference collisions, duplicates, reordering, malformed segment counts, expiration, and 255-part limits
- all three bind modes, incorrect credentials, unmatched responses, EOF/reset recovery, timeouts, rebinds, and cancelled reconnects
- concurrent multipart submissions, sequence allocation, send failures, and acknowledgement correlation
- every response ordering for three-part submissions, reconnects between acknowledged segments, and isolated message timeouts
- transient accept failures, slow bind deadlines, in-progress connect cancellation, and callbacks that block, throw, or send replies
- sustained buffered traffic, dense framing allocation limits, malformed-request error replies, and notification commands without responses
- response error encoding and valid, malformed, Unicode, multipart, and long delivery reports

Run the complete suite in both configurations:

```bash
dotnet test SMSGate.sln --configuration Debug -warnaserror
dotnet test SMSGate.sln --configuration Release -warnaserror
```

## Current Limitations

The implementation is usable, but there are several behaviors worth knowing before adopting it in production:

- Legacy `Gate` connection registries are stored in static global collections, so multiple `Gate` instances share process-wide state. `SmppGateway` owns its outbound clients independently; the server listener remains static.
- Connection configuration and the public registries remain mutable. Set configuration before traffic and avoid modifying the registries directly while connections are active.
- Packed ASCII has no separate septet count in the PDU; a trailing zero septet is treated as padding.
- Packed GSM-7 uses CR padding; a genuine trailing CR can be ambiguous without a separate septet count. Unpacked GSM-7 is recommended for SMPP providers.
- Automatic acknowledgements remain the compatibility default. Set `InboundMessageHandler` to persist or reject each incoming PDU before responding; ordinary event handlers remain observational.

## Development Notes

Build from source with the .NET 10 SDK matching [global.json](https://github.com/kirakosyan/smsgate/blob/master/global.json):

```bash
dotnet restore SMSGate.sln -warnaserror
dotnet build SMSGate.sln -warnaserror
```

The solution contains the library in `Smpp/` and NUnit tests in `Smpp.Tests/`. The fastest feedback loop when extending the library is:

```bash
dotnet test SMSGate.sln -warnaserror
```

That exercises both the command-level tests and the smoke/integration scenarios shipped with the solution. Tests use local TCP listeners and need no SMS provider account or external service. Run configurations sequentially and keep loopback port 2775 free.

To build the NuGet package and verify the example against it locally:

```bash
dotnet pack Smpp/Smpp.csproj --configuration Release -warnaserror --output artifacts
dotnet restore examples/LocalExchange/LocalExchange.csproj --source "$PWD/artifacts" -warnaserror
dotnet run --project examples/LocalExchange/LocalExchange.csproj --configuration Release --no-restore
```

The package includes the SMSGate icon, this README, the MIT license, release notes, XML API documentation, and a companion symbols package with Source Link. GitHub Actions validates both build configurations and the examples before publishing a version tag through NuGet Trusted Publishing. Maintainers can follow the [release guide](https://github.com/kirakosyan/smsgate/blob/master/docs/RELEASING.md). Editable logos, PNG exports, and platform icon attribution are in [the branding assets](https://github.com/kirakosyan/smsgate/tree/master/assets/branding).

## Contributing

Issues and pull requests are welcome. Include a reproducible example for protocol or session bugs, keep changes compatible with the documented API, and run the Debug and Release suites before submitting a change. Existing tests cover wire encoding, session recovery, and local client/server exchanges; add focused coverage when changing those behaviors.

## License

SMSGate is distributed under the [MIT license](https://github.com/kirakosyan/smsgate/blob/master/LICENSE).
