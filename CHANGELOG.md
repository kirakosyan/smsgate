# Changelog

This project uses [Semantic Versioning](https://semver.org/). Released versions are tagged as `vMAJOR.MINOR.PATCH`.

## 1.1.0 - 2026-10-17

- Explicit GSM-7 encoding and decoding with default/extension alphabet support, septet-aware segmentation, and `auto` Unicode fallback on connection submissions.
- Per-PDU submission results with every provider ID, segment number, raw SMPP status, and explicit acknowledgement timeouts.
- Delivery receipt TLVs (`receipted_message_id`, `message_state`, `network_error_code`) on `deliver_sm` and `data_sm`, early-receipt correlation, and complete multipart delivery tracking across reconnects. Shared provider stores support split TX/RX binds, shared multipart IDs, configurable ACCEPTD semantics, and bounded retention.
- Optional client and server TLS using platform certificate validation, cancellation, and handshake deadlines.
- Optional asynchronous inbound acceptance handlers with application-selected IDs, rejection statuses, deadlines, bounded concurrency, and protection against stale responses after reconnects.
- Bounded `SubmitAsync` queues with PDU rate/window controls, structured results, cancellation, explicit pre-send failures, and opt-in retries restricted to explicit whole-attempt throttle rejections. Aggregate events report the final retry outcome; unknown or partially accepted attempts are never automatically retried.
- `ConnectAndBindAsync`, validated client options, instance-owned gateways/clients, automatic reconnect, and graceful draining with sync/async disposal.
- Connection-state and structured diagnostic events, per-send trace context, and BCL metrics for throughput, queue depth, in-flight PDUs, acknowledgements, and delivery. Metric listeners run outside protocol and delivery-store locks.
- Runnable TLS/provider and Generic Host/DI examples, plus an independent Python SMPP peer. Windows and Linux CI validate all examples against the built NuGet package.

Existing APIs, ASCII encoding, automatic acknowledgements, TCP endpoints, and legacy event signatures remain available. `UseGsm7` defaults to false; opt in to match the provider's inbound alphabet. Legacy `SubmitSm` bypasses async queue limits. Queues and delivery tracking are in memory; persistence and duplicate/retry policy across process restarts remain the application's responsibility. The server listener and legacy `Gate` registries remain static.

## 1.0.0

First NuGet release of the existing .NET 10 library, published under the package ID `SMSGate`. The assembly and namespaces remain `Smpp`.

- SMPP 3.4 receiver, transmitter, and transceiver sessions.
- Client `submit_sm`, server `deliver_sm` and `data_sm`, and delivery receipts.
- ASCII, Latin, Unicode, binary, and WAP Push encoding with multipart and payload handling.
- Reconnects, bind and keepalive deadlines, cancellation, and acknowledgement correlation.
- Ordered asynchronous callbacks, stream framing checks, and malformed-request responses.
- NuGet metadata, packaged README and MIT license, XML API documentation, Source Link, and portable debug symbols.
- Debug and Release test automation and a runnable example that consumes the package.

This release packages the existing API; it does not change its runtime behavior or add support for earlier .NET target frameworks.
