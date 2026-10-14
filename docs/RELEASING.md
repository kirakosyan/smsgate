# Releasing SMSGate

The NuGet package is `SMSGate`; the assembly and namespaces are `Smpp`. Versions follow Semantic Versioning, and each release uses a matching Git tag such as `v1.0.0`. NuGet.org versions are immutable: use a new version for any package change.

## Prepare and validate

1. Update `Version` and release notes in `Smpp/Smpp.csproj`, `SMSGateVersion` in the example project, installation examples, and `CHANGELOG.md`.
2. Use the SDK from `global.json`. Restore, run both suites sequentially, and pack only the library:

   ```bash
   dotnet restore SMSGate.sln -warnaserror
   dotnet test SMSGate.sln --configuration Debug --no-restore -warnaserror
   dotnet test SMSGate.sln --configuration Release --no-restore -warnaserror
   dotnet pack Smpp/Smpp.csproj --configuration Release --no-build --no-restore -warnaserror --output artifacts
   dotnet restore examples/LocalExchange/LocalExchange.csproj --source "$PWD/artifacts" -warnaserror
   dotnet run --project examples/LocalExchange/LocalExchange.csproj --configuration Release --no-restore
   ```

3. Confirm that both suites executed tests with no failures, that the example printed `Local SMPP exchange passed.`, and that the `.nupkg` contains `lib/net10.0/Smpp.dll`, XML API documentation, README, license, changelog, and the intended metadata. The `.snupkg` contains portable debug symbols. It must not contain test dependencies or build caches.
4. Commit the release files before the final build. In CI, `CI=true` enables reproducible source paths; the SDK supplies Source Link for GitHub. Confirm that package `repository.commit` matches the release commit. Rebuild if that commit changed.

## Publish with GitHub Actions

Create a NuGet.org API key scoped to publishing `SMSGate` (including creation of the first version), and add it as the GitHub repository Actions secret `NUGET_API_KEY`. Never commit a key or paste it into an issue. A cloud environment's proxy-backed secret is separate from a GitHub Actions repository secret; configure each through its secure settings.

After the release commit is on the repository's default branch and validation passes:

```bash
git tag -a v1.0.0 -m "SMSGate 1.0.0"
git push origin v1.0.0
```

The release workflow checks that the tag matches the package version, runs Debug and Release tests and the package example, uploads build artifacts, pushes the package and accompanying symbols to NuGet.org, and creates a GitHub release. It supports retrying a tag through **Run workflow**; duplicate NuGet versions are skipped. Do not move an existing release tag to different source.

## Publish from a development environment

Use a securely configured `NUGET_API_KEY` scoped to NuGet.org. In Codex cloud, allow `api.nuget.org` and `www.nuget.org` and bind the proxy secret to those destinations. The actual package and symbol upload endpoints are on `www.nuget.org`.

```bash
dotnet nuget push artifacts/SMSGate.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --symbol-api-key "$NUGET_API_KEY"
```

The CLI also uploads the adjacent `.snupkg`. Check the push result and the [NuGet package page](https://www.nuget.org/packages/SMSGate); indexing can take a few minutes. Once public, restore version `1.0.0` in a fresh consumer project from nuget.org and run the example again. A successful local package build does not establish publication.

For repository discovery, use a description such as **Lightweight SMPP 3.4 client and server for .NET: SMS, delivery receipts, multipart messaging, and local integration testing**, a homepage of `https://www.nuget.org/packages/SMSGate`, and topics `smpp`, `sms`, `sms-gateway`, `dotnet`, `csharp`, `messaging`, `telecom`, and `nuget`. Preserve other relevant topics when updating repository settings.
