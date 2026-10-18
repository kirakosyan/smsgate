param(
    [Parameter(Mandatory = $true)][string]$Package,
    [Parameter(Mandatory = $true)][string]$RunnerDirectory,
    [Parameter(Mandatory = $true)][string]$Destination,
    [Parameter(Mandatory = $true)][int]$ExpectedTests
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (Test-Path -LiteralPath $Destination) { throw 'Use a new destination directory.' }
[IO.Directory]::CreateDirectory($Destination) | Out-Null
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
foreach ($folder in @('tests', 'runner', 'consumer')) { [IO.Directory]::CreateDirectory((Join-Path $destinationPath $folder)) | Out-Null }
Copy-Item (Join-Path $root 'Smpp.Tests/bin/Release/net472/*') (Join-Path $destinationPath 'tests') -Recurse
Copy-Item (Join-Path $RunnerDirectory '*') (Join-Path $destinationPath 'runner') -Recurse
Copy-Item (Join-Path $root 'examples/FrameworkExchange/bin/Release/net472/*') (Join-Path $destinationPath 'consumer') -Recurse
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $Package).Path)
try {
    $entry = $archive.GetEntry('lib/net472/Smpp.dll')
    if ($null -eq $entry) { throw 'The package is missing the Framework assembly.' }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destinationPath 'tests/Smpp.dll'), $true)
} finally { $archive.Dispose() }
$libraryHash = (Get-FileHash (Join-Path $destinationPath 'tests/Smpp.dll') -Algorithm SHA256).Hash
if ((Get-FileHash (Join-Path $destinationPath 'consumer/Smpp.dll') -Algorithm SHA256).Hash -ne $libraryHash) {
    throw 'The consumer must be restored and built from this exact package before creating the bundle.'
}
Copy-Item (Join-Path $PSScriptRoot 'qualify-framework.ps1') $destinationPath
@{
    librarySha256 = $libraryHash
    packageSha256 = (Get-FileHash -LiteralPath $Package -Algorithm SHA256).Hash
    expectedTests = $ExpectedTests
} | ConvertTo-Json | Set-Content (Join-Path $destinationPath 'bundle.json') -Encoding UTF8
Write-Output "Framework qualification bundle: $destinationPath"
