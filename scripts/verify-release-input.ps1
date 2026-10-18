param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [Parameter(Mandatory = $true)][string]$PackageSha256
)
$ErrorActionPreference = 'Stop'
if ($env:GITHUB_REF_TYPE -ne 'tag' -or $RunId -notmatch '^\d+$' -or $PackageSha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'A tag, validation run ID, and qualified package SHA-256 are required.' }
$run = & gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$RunId" | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw 'Cannot read the validation run.' }
if ($run.path -ne '.github/workflows/release.yml' -or $run.head_sha -ne $env:GITHUB_SHA -or $run.head_branch -ne $env:GITHUB_REF_NAME -or $run.status -ne 'completed' -or $run.conclusion -ne 'success') {
    throw 'Validation must be a successful Release run for this exact tag and commit.'
}
$jobs = (& gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$RunId/jobs?per_page=100" | ConvertFrom-Json).jobs
if ($LASTEXITCODE -ne 0) { throw 'Cannot read validation jobs.' }
foreach ($name in @('validate', 'validate-framework')) {
    if (@($jobs | Where-Object { $_.name -eq $name -and $_.conclusion -eq 'success' }).Count -ne 1) { throw "Missing successful $name job." }
}
$artifacts = (& gh api "repos/$env:GITHUB_REPOSITORY/actions/runs/$RunId/artifacts?per_page=100" | ConvertFrom-Json).artifacts
if ($LASTEXITCODE -ne 0) { throw 'Cannot read validation artifacts.' }
$packageArtifact = @($artifacts | Where-Object { $_.name -eq 'nuget-packages' -and -not $_.expired })
if ($packageArtifact.Count -ne 1) { throw 'Expected one immutable package artifact from validation.' }
"artifact_id=$($packageArtifact[0].id)" >> $env:GITHUB_OUTPUT
