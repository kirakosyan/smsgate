# Changelog

This project uses [Semantic Versioning](https://semver.org/). Released versions are tagged as `vMAJOR.MINOR.PATCH`.

## Unreleased

- Explicit GSM-7 encoding and decoding with default/extension alphabet support, septet-aware segmentation, and `auto` Unicode fallback on connection submissions.
- Per-PDU submission results with every provider ID, segment number, raw SMPP status, and explicit acknowledgement timeouts.
- Delivery receipt TLVs (`receipted_message_id`, `message_state`, `network_error_code`) on `deliver_sm` and `data_sm`, early-receipt correlation, and complete multipart delivery tracking across reconnects.
- Optional client and server TLS using platform certificate validation, cancellation, and handshake deadlines.
- Optional asynchronous inbound acceptance handlers with application-selected IDs, rejection statuses, deadlines, bounded concurrency, and protection against stale responses after reconnects.
- A runnable example combining TLS, GSM-7 multipart messages, storage before acknowledgement, and per-segment delivery reports.

Existing ASCII encoding, automatic acknowledgements, TCP endpoints, and legacy events remain available. Delivery tracking is in memory; persistence and retry/idempotency policy remain the application's responsibility.

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
