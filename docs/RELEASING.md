# Releasing SMSGate

The NuGet package is `SMSGate`; the assembly and namespaces are `Smpp`. Versions follow Semantic Versioning, and each release uses a matching Git tag such as `v1.1.0`. NuGet.org versions are immutable: use a new version for any package change.

## Prepare and validate

1. Update `Version` and release notes in `Smpp/Smpp.csproj`, `SMSGateVersion` in `examples/LocalExchange/LocalExchange.csproj`, installation examples, integration guides, and `CHANGELOG.md`.
2. Commit the release files before the final build. In CI, `CI=true` enables reproducible source paths; the SDK supplies Source Link for GitHub. Rebuild if the release commit changes.
3. Use the SDK from `global.json`. Restore, run both suites sequentially, and pack only the library. For 1.1.0, validate API compatibility against the published 1.0.0 package:

   ```bash
   dotnet restore SMSGate.sln -warnaserror
   dotnet test SMSGate.sln --configuration Debug --no-restore -warnaserror
   dotnet test SMSGate.sln --configuration Release --no-restore -warnaserror
   dotnet pack Smpp/Smpp.csproj --configuration Release --no-build --no-restore -warnaserror --output artifacts -p:EnablePackageValidation=true -p:PackageValidationBaselineVersion=1.0.0
   dotnet restore examples/LocalExchange/LocalExchange.csproj --source "$PWD/artifacts" -warnaserror
   dotnet run --project examples/LocalExchange/LocalExchange.csproj --configuration Release --no-restore
   dotnet restore examples/ProviderFeatures/ProviderFeatures.csproj --source "$PWD/artifacts" -p:SMSGateVersion=1.1.0 -warnaserror
   dotnet run --project examples/ProviderFeatures/ProviderFeatures.csproj --configuration Release --no-restore -p:SMSGateVersion=1.1.0
   dotnet restore examples/WorkerService/WorkerService.csproj --source "$PWD/artifacts" -p:SMSGateVersion=1.1.0 -warnaserror
   dotnet run --project examples/WorkerService/WorkerService.csproj --configuration Release --no-restore -p:SMSGateVersion=1.1.0
   dotnet restore examples/InteropPeer/InteropPeer.csproj --source "$PWD/artifacts" -p:SMSGateVersion=1.1.0 -warnaserror
   dotnet build examples/InteropPeer/InteropPeer.csproj --configuration Release --no-restore -p:SMSGateVersion=1.1.0 -warnaserror
   python scripts/interop.py examples/InteropPeer/bin/Release/net10.0/InteropPeer.dll
   ```

4. Confirm that both suites executed tests with no failures or unexpected skips, that the local exchange, provider features, hosted service, and both Python peer directions passed, and that API compatibility validation passed without suppressions. Python 3 is required only for the independent peer check and uses its standard library. Use a fresh NuGet package cache when revalidating an already-cached development version.
5. Inspect the `.nupkg` for `lib/net10.0/Smpp.dll`, XML API documentation, README, license, changelog, and the intended metadata. Confirm `repository.commit` matches the release commit and the dependency group is empty. The `.snupkg` contains portable debug symbols. Neither package should contain test dependencies or build caches. CI validates on Windows and Linux; keep both platforms passing before tagging.

## Publish with GitHub Actions

GitHub Actions uses [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) to obtain a short-lived publishing credential. No stored `NUGET_API_KEY` is required.

Configure these matching settings before the first release:

1. In the repository's **Settings > Environments**, create `nuget`. Allow deployment from tags matching `v*` only, and add the environment variable `NUGET_USER` with value `kirakosyan`.
2. In the NuGet.org account's **Trusted Publishing** settings, create `smsgate-github-release` with package owner `kirakosyan`, provider **GitHub Actions**, repository owner `kirakosyan`, repository `smsgate`, workflow file `release.yml`, and environment `nuget`. Enter only the workflow filename, without `.github/workflows/`.
3. Allow **Push new packages and package versions** with the exact package pattern `SMSGate`, including creation of the first version. Leave unlist/relist permission disabled.

The GitHub environment and NuGet policy must use the same environment and workflow names. Other packages and workflows are outside this policy's scope.

After the release commit is on the repository's default branch and validation passes:

```bash
git tag -a v1.1.0 -m "SMSGate 1.1.0"
git push origin v1.1.0
```

The validation job requires the tagged commit to be on `master` and the tag to match the package version. It runs Debug and Release tests and all four package consumer checks above, then uploads the packages. Only after validation succeeds does a separate job enter the `nuget` environment, download that exact artifact by ID, request a temporary credential through `NuGet/login`, push the package and accompanying symbols, and create a GitHub release. Only the publishing job has OIDC token permission. Publishing is serialized across releases.

The workflow supports retrying a tag through **Run workflow**; duplicate NuGet versions are skipped. Select the existing tag when retrying. Branch runs do not publish. Do not move an existing release tag to different source.

## Publish from a development environment

Prefer the Trusted Publishing workflow above. A manual CLI push instead requires a separately configured NuGet API key scoped to publishing only `SMSGate`, including new packages for the first version. Store it securely as `NUGET_API_KEY`; never commit or paste the value into an issue or chat. The GitHub Actions OIDC credential is issued only inside the matching workflow and is not a development-environment secret.

```bash
dotnet nuget push artifacts/SMSGate.1.1.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --symbol-api-key "$NUGET_API_KEY"
```

The CLI also uploads the adjacent `.snupkg`. Check the push result and the [NuGet package page](https://www.nuget.org/packages/SMSGate); indexing can take a few minutes.

## Verify the public package

Confirm that `1.1.0` appears in the [NuGet flat-container index](https://api.nuget.org/v3-flatcontainer/smsgate/index.json), bypassing cached responses if necessary. Download the public `.nupkg`, check its repository commit and contents against the release artifact, and verify the NuGet.org repository signature with `dotnet nuget verify <downloaded-package.nupkg> --all`.

Restore and run consumers from nuget.org using a new, empty package directory. For example, in a Bash shell:

```bash
export NUGET_PACKAGES="$(mktemp -d)"
dotnet restore examples/LocalExchange/LocalExchange.csproj --source https://api.nuget.org/v3/index.json --no-http-cache -warnaserror
dotnet run --project examples/LocalExchange/LocalExchange.csproj --configuration Release --no-restore
dotnet restore examples/ProviderFeatures/ProviderFeatures.csproj --source https://api.nuget.org/v3/index.json --no-http-cache -p:SMSGateVersion=1.1.0 -warnaserror
dotnet run --project examples/ProviderFeatures/ProviderFeatures.csproj --configuration Release --no-restore -p:SMSGateVersion=1.1.0
```

Check that the resolved SMSGate version is `1.1.0` and that restore fetched it from nuget.org. A successful local package build or push does not establish that a new user can restore and run the public package.

For repository discovery, use a description such as **Lightweight SMPP 3.4 client and server for .NET: SMS, delivery receipts, multipart messaging, and local integration testing**, a homepage of `https://www.nuget.org/packages/SMSGate`, and topics `smpp`, `sms`, `sms-gateway`, `dotnet`, `csharp`, `messaging`, `telecom`, and `nuget`. Preserve other relevant topics when updating repository settings.
