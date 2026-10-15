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

GitHub Actions uses [NuGet Trusted Publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) to obtain a short-lived publishing credential. No stored `NUGET_API_KEY` is required.

Configure these matching settings before the first release:

1. In the repository's **Settings > Environments**, create `nuget`. Allow deployment from tags matching `v*` only, and add the environment variable `NUGET_USER` with value `kirakosyan`.
2. In the NuGet.org account's **Trusted Publishing** settings, create `smsgate-github-release` with package owner `kirakosyan`, provider **GitHub Actions**, repository owner `kirakosyan`, repository `smsgate`, workflow file `release.yml`, and environment `nuget`. Enter only the workflow filename, without `.github/workflows/`.
3. Allow **Push new packages and package versions** with the exact package pattern `SMSGate`, including creation of the first version. Leave unlist/relist permission disabled.

The GitHub environment and NuGet policy must use the same environment and workflow names. Other packages and workflows are outside this policy's scope.

After the release commit is on the repository's default branch and validation passes:

```bash
git tag -a v1.0.0 -m "SMSGate 1.0.0"
git push origin v1.0.0
```

The validation job requires the tagged commit to be on `master` and the tag to match the package version. It runs Debug and Release tests and the package example, then uploads the packages. Only after validation succeeds does a separate job enter the `nuget` environment, download that exact artifact by ID, request a temporary credential through `NuGet/login`, push the package and accompanying symbols, and create a GitHub release. Only the publishing job has OIDC token permission. Publishing is serialized across releases.

The workflow supports retrying a tag through **Run workflow**; duplicate NuGet versions are skipped. Select the existing tag when retrying. Branch runs do not publish. Do not move an existing release tag to different source.

## Publish from a development environment

Prefer the Trusted Publishing workflow above. A manual CLI push instead requires a separately configured NuGet API key scoped to publishing only `SMSGate`, including new packages for the first version. Store it securely as `NUGET_API_KEY`; never commit or paste the value into an issue or chat. The GitHub Actions OIDC credential is issued only inside the matching workflow and is not a development-environment secret.

```bash
dotnet nuget push artifacts/SMSGate.1.0.0.nupkg \
  --source https://api.nuget.org/v3/index.json \
  --api-key "$NUGET_API_KEY" --symbol-api-key "$NUGET_API_KEY"
```

The CLI also uploads the adjacent `.snupkg`. Check the push result and the [NuGet package page](https://www.nuget.org/packages/SMSGate); indexing can take a few minutes. Once public, restore version `1.0.0` in a fresh consumer project from nuget.org and run the example again. A successful local package build does not establish publication.

For repository discovery, use a description such as **Lightweight SMPP 3.4 client and server for .NET: SMS, delivery receipts, multipart messaging, and local integration testing**, a homepage of `https://www.nuget.org/packages/SMSGate`, and topics `smpp`, `sms`, `sms-gateway`, `dotnet`, `csharp`, `messaging`, `telecom`, and `nuget`. Preserve other relevant topics when updating repository settings.
