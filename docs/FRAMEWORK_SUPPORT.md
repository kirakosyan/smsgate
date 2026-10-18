# .NET Framework support

SMSGate 1.2.0 adds `net472` alongside `net10.0`. Framework 4.7.2 is the minimum target; 4.8 and 4.8.1 applications use that assembly through Framework's in-place compatibility. Framework is Windows-only. The operating system must remain supported for the runtime you deploy.

## Application setup

Use PackageReference so NuGet restores the compatibility dependencies. A minimal SDK-style console project is:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net472</TargetFramework>
    <LangVersion>latest</LangVersion>
    <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
    <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="SMSGate" Version="1.2.0" />
  </ItemGroup>
</Project>
```

The modern options use `init` accessors, so use C# 9 or later. C# language support is separate from the installed Framework runtime. Use the pinned .NET SDK in `global.json` to build this repository. Reference assemblies allow cross-platform compilation; running Framework applications still requires Windows and the installed Framework runtime.

`SmppGateway`, `ConnectAndBindAsync`, `SubmitAsync`, multipart delivery tracking, metrics, and inbound acknowledgements work on both targets. Put the README quick start inside `static async Task Main()`. `await using` calls the gateway's asynchronous shutdown; older application code can explicitly await `StopAsync` and then call `Dispose`. Classic ASP.NET applications should manage redirects in the application's `web.config` and test within their actual host; the console checks do not qualify every hosting model.

Only the Framework target depends on these Microsoft packages:

| Package | Version | Purpose |
| --- | --- | --- |
| Microsoft.Bcl.AsyncInterfaces | 10.0.13 | Async disposal contracts |
| System.Threading.Channels | 10.0.13 | Bounded asynchronous queues |
| System.Diagnostics.DiagnosticSource | 10.0.13 | Tracing and metrics |
| System.Memory | 4.6.3 | Portable span and binary operations |

NuGet also restores their transitive dependencies. `Microsoft.NETFramework.ReferenceAssemblies.net472` 1.0.3 is private and build-only. The `net10.0` dependency group stays empty. Deploy the application's generated configuration and dependency assemblies together; do not copy only `Smpp.dll` into an existing application.

The new Framework assembly is strong-named with public key token `4a6828ca32ee5c95`. The signed FrameworkExchange package consumer builds with warnings treated as errors. Its signing key is checked in for reproducible open-source builds; strong naming supplies assembly identity, not publisher authentication. Keep this key stable. The existing .NET 10 assembly retains its unsigned identity to preserve compatibility with already compiled consumers.

## TLS on both targets

Shared source can use the portable options:

```csharp
client.TlsOptions = new SmppTlsClientOptions
{
    TargetHost = "smpp.provider.example",
    CheckCertificateRevocation = true
};

Server.StartAsync("0.0.0.0", 2776, new SmppTlsServerOptions
{
    ServerCertificate = certificate // An X509Certificate2 with its private key.
});
```

`SslProtocols.None` delegates protocol selection to Windows policy on Framework. Certificate chain and hostname validation apply by default. An optional `CertificateValidationCallback` replaces certificate acceptance, so an application-supplied policy must check all of its own trust requirements. No fallback to unencrypted TCP occurs after TLS failure. Cancellation/deadlines dispose pending Framework transport or TLS operations, which closes that connection.

| API | .NET 10 | Framework |
| --- | --- | --- |
| `Client.TlsOptions`, `SmppClientOptions.TlsOptions` | Existing `SslClientAuthenticationOptions` type | `SmppTlsClientOptions` |
| TLS `Server.StartAsync` overload | Existing `SslServerAuthenticationOptions` type | `SmppTlsServerOptions` |
| Portable options above | Implicitly converted to the native options | Used directly |
| TLS features | Native .NET options, including custom chain policies and certificate selection | Fixed server certificate, optional client certificates, protocols, revocation, validation callback |

The existing .NET 10 API remains compatible with 1.1.1. Framework's older TLS stack cannot expose every modern .NET authentication option. TLS protocol availability depends on the installed runtime, OS, and policy; targeting 4.7.2 does not itself enable TLS 1.3.

Configure portable options fully before assigning them. Both targets snapshot scalar settings and copy the client certificate collection at assignment (or when starting a listener). Later changes to the original options or additions to its collection do not affect the assigned settings. Certificate objects and callback delegates remain shared, so keep their resources valid for the connection/listener lifetime. On .NET 10 the conversion creates native options; assigning an existing native options object retains the original .NET 10 reference behavior.

These are source-compatible target-specific APIs, not a binary migration bridge. A precompiled `net472` class library that calls TLS members must be rebuilt for modern .NET: the property/parameter types and Framework assembly identity differ. Do not rely on a `NU1701` fallback to load such a binary into a .NET 10 application. Multi-target shared source and let NuGet choose the matching SMSGate assembly.

## Validation

The shared NUnit suite runs under both targets. Windows CI executes Framework tests on its installed newer Framework runtime, and runs `examples/FrameworkExchange` against a packed NuGet package in an isolated package cache. Linux CI builds both assemblies and runs the .NET 10 tests. Framework tests include TLS trust/hostname rejection, stalled-handshake cancellation, framing, reconnects, queue/window/rate limits, multipart tracking, metrics, and shutdown.

Minimum-runtime qualification must additionally run the package assembly on actual Framework 4.7.2 (registry Release `461808` or `461814`). A `net472` build executed on 4.8.1 is a later-runtime check. Use `scripts/qualify-framework.ps1 -RequireFramework472` with a prepared bundle containing:

- `tests/`: Release `net472` test output, with `Smpp.dll` replaced by the assembly extracted from the package.
- `runner/`: the NUnit.ConsoleRunner 3.22.0 `tools` directory.
- `consumer/`: the package-restored FrameworkExchange Release output.
- `bundle.json`: `librarySha256`, `packageSha256`, and `expectedTests` values for this build.

The script verifies the actual registry version, package assembly identity, full test count with zero skips, and a separate consumer exchange. It records the runtime and results without setting a legacy JIT override. Keep its `runtime.json`, `tests.xml`, and consumer output with release evidence. Runtime qualification is tracked in [FRAMEWORK_VALIDATION.md](FRAMEWORK_VALIDATION.md).

## Run the local package example

On Windows, to test a package built from this checkout:

```powershell
dotnet pack Smpp/Smpp.csproj -c Release -o artifacts
./scripts/restore-framework-example.ps1 -PackageDirectory artifacts
dotnet run --project examples/FrameworkExchange -c Release --no-restore
```

The example uses `SmppGateway`, two GSM-7 segments, per-segment provider IDs, complete delivery tracking, and graceful shutdown. It needs no SMS provider account.

The restore helper creates a fresh package directory for every invocation, so repeated builds of the same package version cannot reuse an older cached SMSGate assembly. The resulting assets file records that directory for the subsequent `--no-restore` run.

References: [Framework version compatibility](https://learn.microsoft.com/dotnet/framework/install/versions-and-dependencies), [Framework TLS guidance](https://learn.microsoft.com/dotnet/framework/network-programming/tls), [Framework lifecycle](https://learn.microsoft.com/lifecycle/faq/dotnet-framework).
