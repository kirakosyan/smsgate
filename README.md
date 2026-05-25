# SMSGate

SMSGate is a lightweight SMPP client and server library for .NET. The solution currently targets `net10.0` and exposes a small event-driven API for establishing SMPP sessions, exchanging messages, sending delivery reports, and testing SMPP flows locally.

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

## Requirements

- .NET 10 SDK

The project files in this repository target `net10.0`. Build artifacts for other frameworks may exist in `bin/` or `obj/`, but they are not the source-of-truth targets.

## Build

```bash
dotnet restore
dotnet build SMSGate.sln
```

## Test

```bash
dotnet test SMSGate.sln
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

// Give the listener a brief moment to start.
Thread.Sleep(500);

var client = gate.AddClientConnection("client-main", "127.0.0.1", 2775, "sysId", "sysPass");
client.debug = true;
client.direction_in = true;
client.direction_out = true;

if (!client.Connect())
{
	Console.WriteLine("Client could not connect.");
	return;
}

Thread.Sleep(1000);

client.SubmitSm(1, "47975091981", "97509181", "Hello from client", "ascii", true);
server.SubmitSm(2, "4755555", "47666666", "Hello from server", "ascii", true);

server.SubmitDeliveryReport("1", Common.MessageStatus.delivered_ACK_received);

Thread.Sleep(1000);

client.Quit();
Server.Stop();
```

## Connection Model

### Client

Create a client with `Gate.AddClientConnection(...)` and then call `Connect()`.

Binding mode is selected from these flags before calling `Connect()`:

- `direction_in = true`, `direction_out = true` -> bind as transceiver
- `direction_in = false`, `direction_out = true` -> bind as transmitter
- `direction_in = true`, `direction_out = false` -> bind as receiver

### Server

Create one or more logical server channels with `Gate.AddServerConnection(...)`, then start the shared listener with `Server.StartAsync(ip, port)`.

Each registered server channel acts as an allowed credential pair. During bind, the listener authenticates the inbound `system_id` and `password` against the configured server channels and routes the accepted session to the matching server object.

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
- `3` - duplicate sequence number

For server-originated traffic:

- `use_deliver_sm = false` sends `data_sm`
- `use_deliver_sm = true` sends `deliver_sm`

## Body Formats

The library maps these body format names internally:

- `ascii`
- `binary`
- `latin`
- `unicode`
- `wap_push`

## Key Configuration Fields

Most tuning is done by assigning public fields on `Client` or `Server` instances before traffic begins.

| Field | Default | Purpose |
| --- | --- | --- |
| `debug` | `false` | Includes raw PDU payloads in `ChannelEvent` logs |
| `system_type` | `""` | SMPP system type |
| `source_ton` / `source_npi` | `0` / `0` | Source addressing configuration |
| `destination_ton` / `destination_npi` | `0` / `0` | Destination addressing configuration |
| `registered_delivery` | `1` | Requests delivery reporting when supported |
| `large_message` | `1` | Large-message handling: multipart, payload, truncate, or fail |
| `use_deliver_sm` | `false` | Makes server-originated sends use `deliver_sm` instead of `data_sm` |
| `use8bit` | `false` | Sends payload in 8-bit mode |
| `split_base_ascii` | `153` | ASCII multipart split threshold |
| `multypart_delay` | `100` | Delay in milliseconds between multipart segments |
| `reconnectDelay` | `10000` | Delay before reconnect attempts in most reconnect paths |
| `keep_alive` | `60000` | Intended enquire_link interval |
| `timeout` | `10000` | Intended command timeout |

## Events

`GateEvents` is the main integration surface for application code.

| Event | Purpose |
| --- | --- |
| `Event` | General informational and error messages |
| `ChannelEvent` | Low-level SMPP channel activity, including bind, send, receive, and disconnect messages |
| `MessageStatusEvent` | Message lifecycle changes for submitted messages |
| `MessageDeliveryReportEvent` | Delivery reports parsed from inbound `deliver_sm` PDUs |
| `NewMessageEvent` | Application-facing inbound messages with sender, recipient, body, and format |

Useful message states come from `Common.MessageStatus`, including `submitted_waiting_for_ACK`, `sent`, `delivered_ACK_received`, `queued`, `timeout`, `rejected`, and `message_undeliverable`.

## Tested Scenarios

The current test suite covers:

- command encode/decode for core SMPP PDUs
- client/server smoke tests with two parallel client connections
- `submit_sm`, `deliver_sm`, and `data_sm` message flow
- Unicode and Latin payload handling
- multipart long-message handling
- delivery report generation and parsing

## Current Limitations

The implementation is usable, but there are several behaviors worth knowing before adopting it in production:

- Connection registries are stored in static global collections, so multiple `Gate` instances share process-wide state.
- `Server.StartAsync(...)` is fire-and-forget and does not expose a readiness signal.
- Accepted server sessions are routed using `system_id`; duplicate server registrations with the same `system_id` can therefore become ambiguous.

## Development Notes

This repository is currently a source-first library rather than a packaged SDK. If you plan to extend it, the fastest feedback loop is:

```bash
dotnet test SMSGate.sln
```

That exercises both the command-level tests and the smoke/integration scenarios shipped with the solution.
