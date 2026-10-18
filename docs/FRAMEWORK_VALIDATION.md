# Framework validation evidence

Development validation for the upcoming SMSGate 1.2.0, performed on 2026-10-18. This is not a published release qualification. Rebuild and retain evidence for the exact committed release package before tagging.

| Environment | Configuration | Result |
| --- | --- | --- |
| Windows x64, .NET 10 | Debug and Release | 1,048 tests passed per configuration; zero failures/skips |
| Windows x64, Framework 4.8.1 (Release 534296, version 4.8.10008) | `net472`, Debug and Release | 1,052 tests passed per configuration; zero failures/skips |
| Windows x64, Framework 4.8.1 | Release package bundle using NUnit console | 1,052 tests passed; zero failures/skips; separate FrameworkExchange consumer passed |
| Linux x64, .NET 10 SDK container 10.0.402 | Debug and Release | 1,048 tests passed per configuration; zero failures/skips |
| Actual Framework 4.7.2 | Release package bundle | Pending isolated Windows Server 2019 execution |

Additional checks passed:

- Both `net10.0` and `net472` build with warnings treated as errors.
- NuGet package API compatibility against SMSGate 1.1.1 passes without suppressions for the existing .NET 10 API.
- The .NET 10 package dependency group is empty. Framework has the four documented Microsoft compatibility dependencies; reference assemblies remain private.
- An empty-cache Framework package restore and modern async multipart/delivery/shutdown exchange passed.
- Linux package consumers passed: LocalExchange, ProviderFeatures (TLS and stored acknowledgements), WorkerService, and the independent Python SMPP peer in both directions.
- NuGet vulnerability audit reported no vulnerable direct or transitive library dependencies.

The tested development package's SHA-256 is `B98CA5316B0700F6B18B93444694F091A5C13DBF8DB7AB393D0CCAF3E5C604C7`. Its extracted Framework assembly's SHA-256 is `06ABB7AD847AACE5C27DFD03A5358930766B7F1340418155793F9D05464DCEA8`. These identify the local development artifact, whose repository metadata points to the pre-change base commit; they must not be used as final release-commit evidence.

Local raw reports are under ignored `artifacts/framework-support/`: TRX reports, `qualified-481-fixed/runtime.json`, NUnit `tests.xml`, and package-consumer output. Use [the qualification scripts](FRAMEWORK_SUPPORT.md#validation) to reproduce the Framework package checks and enforce the actual 4.7.2 registry release when testing the minimum runtime.
