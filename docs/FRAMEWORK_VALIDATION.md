# Framework validation evidence

Historical development validation for SMSGate 1.2.0, performed on 2026-10-18, before the final review fixes and Framework signing. These results describe the code commit and development artifact identified below. The final release's canonical package, minimum-runtime report, hashes, and public-feed verification are retained with the [1.2.0 GitHub release](https://github.com/kirakosyan/smsgate/releases/tag/v1.2.0). Follow [RELEASING.md](RELEASING.md) to qualify the exact tag artifact before publishing; do not substitute these older reports.

| Environment | Configuration | Result |
| --- | --- | --- |
| Hosted Windows x64, .NET 10 | Debug and Release | 1,049 tests passed per configuration; zero failures/skips |
| Hosted Windows x64, Framework 4.8.1 (Release 533509) | `net472`, Debug and Release | 1,053 tests passed per configuration; zero failures/skips |
| Local Windows x64, Framework 4.8.1 (Release 534296, version 4.8.10008) | Release package bundle using NUnit console | 1,053 tests passed; zero failures/skips; separate FrameworkExchange consumer passed |
| Hosted Linux x64, .NET 10 | Debug and Release | 1,049 tests passed per configuration; zero failures/skips |
| Isolated Windows Server 2019 x64, actual Framework 4.7.2 (Release 461814, version 4.7.03190) | Release package bundle using NUnit console, default JIT | 1,053 tests passed; zero failures/skips; separate FrameworkExchange consumer passed |

Hosted [CI run 39137072589](https://github.com/kirakosyan/smsgate/actions/runs/39137072589) passed all 6,302 test executions and package consumers on code commit `f469a8a209d489f0572b641e855bee58f5496335`.

Additional checks passed:

- Both `net10.0` and `net472` build with warnings treated as errors.
- NuGet package API compatibility against SMSGate 1.1.1 passes without suppressions for the existing .NET 10 API.
- The .NET 10 package dependency group is empty. Framework has the four documented Microsoft compatibility dependencies; reference assemblies remain private.
- An empty-cache Framework package restore and modern async multipart/delivery/shutdown exchange passed.
- Windows and Linux package consumers passed: LocalExchange, ProviderFeatures (TLS and stored acknowledgements), WorkerService, and the independent Python SMPP peer in both directions. Windows also passed FrameworkExchange.
- NuGet vulnerability audit reported no vulnerable direct or transitive library dependencies.

The local qualification package was built from code commit `f469a8a209d489f0572b641e855bee58f5496335`. Its SHA-256 is `240DD59CEE7F2A722F9F84F844C16DF4F7FEFB8823689EE00A2446066FDD4E8F`; its extracted Framework assembly's SHA-256 is `DBBC71FC96D1971FD3BA3E31B2BACEEAAADFD1569B000CB19F95EEE8FE865237`. These identify this development artifact. Repeat qualification for the exact final release package after the release commit is established.

The minimum-runtime suite ran in an isolated Windows Server 2019 Evaluation VM using software emulation. It passed all 1,053 tests in 132.683 seconds, then passed the separate package consumer. No legacy JIT override was set. The minimum and newer-runtime package checks used the same package and Framework assembly hashes above.

Local raw reports are under ignored `artifacts/framework-support/`: TRX reports, `qualified-472-final/runtime.json`, `qualified-481-final/runtime.json`, NUnit `tests.xml`, and package-consumer output. Hosted reports and packages are attached to the linked CI run. Use [the qualification scripts](FRAMEWORK_SUPPORT.md#validation) to reproduce the Framework package checks and enforce the actual 4.7.2 registry release when testing the minimum runtime.
