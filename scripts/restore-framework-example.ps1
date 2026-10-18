param(
    [Parameter(Mandatory = $true)][string]$PackageDirectory,
    [string]$Version = '1.2.0'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
$config = Join-Path $root 'artifacts/framework-example.nuget.config'
[IO.Directory]::CreateDirectory((Split-Path $config -Parent)) | Out-Null
$escapedFeed = [Security.SecurityElement]::Escape($feed)
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"local`" value=`"$escapedFeed`"/><add key=`"nuget.org`" value=`"https://api.nuget.org/v3/index.json`"/></packageSources></configuration>")
& dotnet restore (Join-Path $root 'examples/FrameworkExchange/FrameworkExchange.csproj') --configfile $config "-p:SMSGateVersion=$Version" -warnaserror
if ($LASTEXITCODE -ne 0) { throw 'Framework example restore failed.' }
