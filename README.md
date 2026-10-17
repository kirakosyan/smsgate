# SMSGate - SMPP client and server for .NET

[![CI](https://github.com/kirakosyan/smsgate/actions/workflows/ci.yml/badge.svg)](https://github.com/kirakosyan/smsgate/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/SMSGate.svg)](https://www.nuget.org/packages/SMSGate)
[![MIT license](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/kirakosyan/smsgate/blob/master/LICENSE)

SMSGate is a lightweight, dependency-free SMPP 3.4 client and server library for **.NET 10**. Use its event-driven API to send and receive SMS, process delivery receipts, handle multipart messages, and test SMPP sessions locally.

The development branch also includes opt-in GSM-7 encoding, complete multipart delivery tracking, TLS, and application-controlled acknowledgements. These additions are not in the published 1.0.0 package. See the [provider integration guide](https://github.com/kirakosyan/smsgate/blob/master/docs/PROVIDER_INTEGRATION.md) and the [runnable TLS example](https://github.com/kirakosyan/smsgate/tree/master/examples/ProviderFeatures).

## Install

```bash
dotnet add package SMSGate --version 1.0.0
```

The NuGet package is named `SMSGate`. The assembly and namespaces remain `Smpp`, so application code uses `using Smpp;` and `using Smpp.Events;`. This release targets `net10.0`; it does not target .NET Framework or earlier .NET versions.

See the [changelog](https://github.com/kirakosyan/smsgate/blob/master/CHANGELOG.md) for release details and the [local exchange example](https://github.com/kirakosyan/smsgate/tree/master/examples/LocalExchange) for a complete runnable client/server exchange with Unicode messages and a delivery receipt.

## Overview

The library is organized around three main types:

- `Gate` is the entry point used to register connections and share a single `GateEvents` instance.
- `Client` opens an outbound SMPP session and binds as receiver, transmitter, or transceiver depending on configuration.
- `Server` registers accepted credentials and hosts a static TCP listener that authenticates inbound binds.

Out of the box, the implementation supports:

- outbound `submit_sm`
- server-originated `deliver_sm` or `data_sm`
- delivery reports via `deliver_sm`
- multipart and long-message handling
- ASCII, Latin, Unicode, binary, and WAP Push body formats
- command-level logging and application callbacks via events

## Solution Layout

- `Smpp/` - the SMPP library
- `Smpp.Tests/` - NUnit-based command and integration tests
- `examples/LocalExchange/` - a runnable example that consumes the NuGet package
- `examples/ProviderFeatures/` - validated TLS, GSM-7 multipart delivery, and storage before acknowledgement

## Requirements

- .NET 10 SDK matching [global.json](https://github.com/kirakosyan/smsgate/blob/master/global.json) for development

Applications consuming the package require .NET 10. Building and running the test suite needs no SMS provider account or external service: integration tests use local TCP listeners. Run test configurations sequentially and keep loopback port 2775 free.

The project files in this repository target `net10.0`. Build artifacts for other frameworks may exist in `bin/` or `obj/`, but they are not the source-of-truth targets.

## Build

```bash
dotnet restore
dotnet build SMSGate.sln -warnaserror
```

## Test

```bash
dotnet test SMSGate.sln -warnaserror
```

## Quick Start

The example below starts an in-process SMPP server, connects a client, subscribes to the available events, sends messages in both directions, and shuts everything down cleanly.

```csharp
using System;
using System.Threading;
using Smpp;
using Smpp.Events;

var eventsHub = new GateEvents();

eventsHub.Event += (level, description) =>
{
	Console.WriteLine($"[{level}] {description}");
};

eventsHub.ChannelEvent += (channelName, description, pdu) =>
{
	Console.WriteLine($"[{channelName}] {description}");
};

eventsHub.MessageStatusEvent += (messageId, status, responseMessageId) =>
{
	Console.WriteLine($"Message {messageId}: {status} ({responseMessageId})");
};

eventsHub.MessageDeliveryReportEvent += (responseMessageId, status) =>
{
	Console.WriteLine($"Delivery report {responseMessageId}: {status}");
};

eventsHub.NewMessageEvent += (channelName, messageId, sender, recipient, body, bodyFormat, registeredDelivery) =>
{
	Console.WriteLine($"[{channelName}] {sender} -> {recipient}: {body}");
};

var gate = new Gate(eventsHub);

// Register credentials that the server will accept.
var server = gate.AddServerConnection("server-main", "sysId", "sysPass");
server.use_deliver_sm = true;
server.registered_delivery = 1;

Server.StartAsync("127.0.0.1", 2775);

var client = gate.AddClientConnection("client-main", "127.0.0.1", 2775, "sysId", "sysPass");
client.debug = true;
client.direction_in = true;
client.direction_out = true;

if (!client.Connect() ||
    !SpinWait.SpinUntil(() => client.connected, TimeSpan.FromSeconds(10)))
{
    Console.WriteLine("Client could not bind.");
    client.Quit();
    Server.Stop();
    return;
}

client.SubmitSm(1, "47975091981", "97509181", "Hello from client", "ascii", true);
server.SubmitSm(2, "4755555", "47666666", "Hello from server", "ascii", true);

server.SubmitDeliveryReport("1", Common.MessageStatus.delivered_ACK_received);

Thread.Sleep(1000);

client.Quit();
Server.Stop();
```

## Connection Model

### Client

Create a client with `Gate.AddClientConnection(...)` and then call `Connect()` or await `ConnectAsync()`. A true return value means the TCP connection opened and the bind request was sent. Wait for `connected == true` before submitting messages; it becomes true only after the matching successful bind response. Concurrent connect calls share one attempt. Connection attempts run outside the session lock, and `Quit()` cancels an in-progress attempt.

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

Both `Client` and `Server` expose `SubmitSm(...)`.

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

Most tuning is done by assigning public fields on `Client` or `Server` instances before traffic begins.

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

- Connection registries are stored in static global collections, so multiple `Gate` instances share process-wide state.
- Connection configuration and the public registries remain mutable. Set configuration before traffic and avoid modifying the registries directly while connections are active.
- Packed ASCII has no separate septet count in the PDU; a trailing zero septet is treated as padding.
- Packed GSM-7 uses CR padding; a genuine trailing CR can be ambiguous without a separate septet count. Unpacked GSM-7 is recommended for SMPP providers.
- Automatic acknowledgements remain the compatibility default. Set `InboundMessageHandler` to persist or reject each incoming PDU before responding; ordinary event handlers remain observational.

## Development Notes

The fastest feedback loop when extending the library is:

```bash
dotnet test SMSGate.sln -warnaserror
```

That exercises both the command-level tests and the smoke/integration scenarios shipped with the solution.

To build the NuGet package and verify the example against it locally:

```bash
dotnet pack Smpp/Smpp.csproj --configuration Release -warnaserror --output artifacts
dotnet restore examples/LocalExchange/LocalExchange.csproj --source "$PWD/artifacts" -warnaserror
dotnet run --project examples/LocalExchange/LocalExchange.csproj --configuration Release --no-restore
```

The package includes this README, the MIT license, release notes, XML API documentation, and a companion symbols package with Source Link. GitHub Actions validates both build configurations and the example before publishing a version tag through NuGet Trusted Publishing. Maintainers can follow the [release guide](https://github.com/kirakosyan/smsgate/blob/master/docs/RELEASING.md).

## Contributing

Issues and pull requests are welcome. Include a reproducible example for protocol or session bugs, keep changes compatible with the documented API, and run the Debug and Release suites before submitting a change. Existing tests cover wire encoding, session recovery, and local client/server exchanges; add focused coverage when changing those behaviors.

## License

SMSGate is distributed under the [MIT license](https://github.com/kirakosyan/smsgate/blob/master/LICENSE).
