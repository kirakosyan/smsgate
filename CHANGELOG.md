# Changelog

This project uses [Semantic Versioning](https://semver.org/). Released versions are tagged as `vMAJOR.MINOR.PATCH`.

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
