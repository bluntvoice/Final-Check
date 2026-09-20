param(
    [switch] $SkipRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    Write-Host 'Final Check Verification'
    if (-not $SkipRestore) {
        & dotnet restore FinalCheck.sln
        if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
        Write-Host 'Restore                PASS'
    }

    & dotnet build FinalCheck.sln --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
    Write-Host 'Build                  PASS'

    & dotnet test FinalCheck.sln --configuration Release --no-build --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Solution tests failed.' }
    Write-Host 'Unit + Headless Tests  PASS'

    $raw = & dotnet run --project tools/FinalCheck.Verification/FinalCheck.Verification.csproj `
        --configuration Release --no-build --no-restore -- verify --format json
    $harnessExit = $LASTEXITCODE
    if ($harnessExit -ne 0) {
        $raw | Out-Host
        throw "Verification Harness failed with exit code $harnessExit."
    }
    $report = ($raw -join "`n") | ConvertFrom-Json
    if (-not $report.success -or @($report.checks | Where-Object status -ne 'pass').Count -gt 0) {
        $raw | Out-Host
        throw 'Verification Harness reported a failed or skipped check.'
    }
    foreach ($check in $report.checks) {
        Write-Host ('{0,-22} PASS' -f $check.name)
    }
    Write-Host ('Harness duration       {0} ms' -f $report.durationMs)
    Write-Host 'Overall                PASS'
    exit 0
}
catch {
    Write-Error $_
    Write-Host 'Overall                FAIL'
    exit 1
}
finally {
    Pop-Location
}
