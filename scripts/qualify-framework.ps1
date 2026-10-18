param(
    [Parameter(Mandatory = $true)][string]$BundleDirectory,
    [Parameter(Mandatory = $true)][string]$ResultsDirectory,
    [switch]$RequireFramework472
)
$ErrorActionPreference = 'Stop'
[IO.Directory]::CreateDirectory($ResultsDirectory) | Out-Null
$report = [ordered]@{ passed = $false; release = $null; version = $null; x64 = [Environment]::Is64BitProcess; jit = 'default'; error = $null }
try {
    $runtime = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full'
    $report.release = $runtime.Release
    $report.version = $runtime.Version
    if ($runtime.Release -lt 461808) { throw 'Framework 4.7.2 or later is required.' }
    if ($RequireFramework472 -and $runtime.Release -notin @(461808, 461814)) { throw 'This is not an actual Framework 4.7.2 runtime.' }
    if (-not $report.x64) { throw 'Run qualification in x64 Windows PowerShell.' }
    $manifest = Get-Content (Join-Path $BundleDirectory 'bundle.json') -Raw | ConvertFrom-Json
    $library = Join-Path $BundleDirectory 'tests/Smpp.dll'
    $report.librarySha256 = (Get-FileHash $library -Algorithm SHA256).Hash
    if ($report.librarySha256 -ne $manifest.librarySha256) { throw 'Test library does not match the package manifest.' }
    $report.packageSha256 = $manifest.packageSha256
    & (Join-Path $BundleDirectory 'runner/nunit3-console.exe') (Join-Path $BundleDirectory 'tests/Smpp.Tests.dll') "--work=$ResultsDirectory" '--result=tests.xml' '--workers=0' '--agents=1' '--noheader'
    if ($LASTEXITCODE -ne 0) { throw "Framework tests failed: $LASTEXITCODE" }
    [xml]$results = Get-Content (Join-Path $ResultsDirectory 'tests.xml') -Raw
    $report.tests = [int]$results.'test-run'.total
    if ($results.'test-run'.result -ne 'Passed' -or $report.tests -ne $manifest.expectedTests -or [int]$results.'test-run'.skipped -ne 0) { throw 'Unexpected test count, skips, or failures.' }
    $consumerLibrary = Join-Path $BundleDirectory 'consumer/Smpp.dll'
    if ((Get-FileHash $consumerLibrary -Algorithm SHA256).Hash -ne $report.librarySha256) { throw 'Consumer does not use the tested package assembly.' }
    & (Join-Path $BundleDirectory 'consumer/FrameworkExchange.exe') | Tee-Object (Join-Path $ResultsDirectory 'consumer.txt')
    if ($LASTEXITCODE -ne 0) { throw "Package consumer failed: $LASTEXITCODE" }
    $report.passed = $true
} catch { $report.error = $_.Exception.ToString(); throw }
finally { $report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $ResultsDirectory 'runtime.json') -Encoding UTF8 }
