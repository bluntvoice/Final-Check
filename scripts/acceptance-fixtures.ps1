[CmdletBinding()]
param(
    [ValidateSet('Generate', 'Verify')]
    [string]$Action = 'Generate',
    [string]$OutputDirectory,
    [switch]$NoBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'tools/FinalCheck.AcceptanceFixtures/FinalCheck.AcceptanceFixtures.csproj'
$assemblyPath = Join-Path $repositoryRoot 'tools/FinalCheck.AcceptanceFixtures/bin/Release/net10.0/FinalCheck.AcceptanceFixtures.dll'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    if ($Action -eq 'Verify') { throw 'Verify requires -OutputDirectory.' }
    $OutputDirectory = Join-Path $repositoryRoot ('artifacts/acceptance-fixtures/' + [guid]::NewGuid().ToString('N'))
}
if (-not [IO.Path]::IsPathFullyQualified($OutputDirectory)) { throw 'OutputDirectory must be absolute.' }
if ($Action -eq 'Generate' -and (Test-Path -LiteralPath $OutputDirectory)) {
    throw 'Generation requires a new directory; existing content is never overwritten.'
}

$previousNotify = $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE
try {
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
    if (-not $NoBuild) {
        & dotnet restore $projectPath --disable-parallel --verbosity quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Fixture tool restore failed.' }
        & dotnet build $projectPath -c Release --no-restore -m:1 /p:UseSharedCompilation=false --verbosity quiet | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Fixture tool build failed.' }
    }
    if (-not (Test-Path -LiteralPath $assemblyPath)) { throw 'Build the fixture tool before using -NoBuild.' }
    $result = & dotnet $assemblyPath $Action.ToLowerInvariant() $OutputDirectory
    $toolExitCode = $LASTEXITCODE
    if ($toolExitCode -ne 0) { throw "Fixture operation failed (exit $toolExitCode): $result" }
    $report = $result | ConvertFrom-Json
    [pscustomobject]@{ OutputDirectory = $OutputDirectory; Result = $report } | ConvertTo-Json -Depth 8
} finally {
    $env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = $previousNotify
}
