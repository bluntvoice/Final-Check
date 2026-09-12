[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$versionScript = Join-Path $PSScriptRoot "version.ps1"
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("finalcheck-version-tests-" + [guid]::NewGuid().ToString('N'))

function Invoke-VersionScript([string[]]$Arguments, [bool]$ShouldSucceed = $true) {
    $output = & (Get-Process -Id $PID).Path -NoProfile -File $versionScript @Arguments 2>&1
    $succeeded = $LASTEXITCODE -eq 0
    if ($succeeded -ne $ShouldSucceed) {
        throw "version.ps1 expectation failed. Args: $($Arguments -join ' '); output: $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

try {
    New-Item -ItemType Directory -Path (Join-Path $tempRoot 'src\Sample') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot '..\Directory.Build.props') -Destination (Join-Path $tempRoot 'Directory.Build.props')
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $tempRoot 'src\Sample\Sample.csproj') -Encoding utf8NoBOM

    $props = Join-Path $tempRoot 'Directory.Build.props'
    $null = Invoke-VersionScript @('set', '0.1.0-alpha.0', '-PropsPath', $props)
    $printed = @(Invoke-VersionScript @('print', '-PropsPath', $props))
    if (([string]$printed[-1]).Trim() -ne '0.1.0-alpha.0') { throw "print returned an unexpected version." }

    $null = Invoke-VersionScript @('check', '-PropsPath', $props)
    $null = Invoke-VersionScript @('assert-not-lower', '0.1.0-beta.1', '-PropsPath', $props)
    $null = Invoke-VersionScript @('assert-not-lower', '0.0.9', '-PropsPath', $props) $false
    $null = Invoke-VersionScript @('set', '0.1.0-beta.01', '-PropsPath', $props) $false
    $null = Invoke-VersionScript @('set', '0.2.0-beta.1', '-PropsPath', $props)
    $updated = @(Invoke-VersionScript @('print', '-PropsPath', $props))
    if (([string]$updated[-1]).Trim() -ne '0.2.0-beta.1') { throw "set did not persist the target version." }
    $null = Invoke-VersionScript @('assert-not-lower', '0.2.0-beta.0', '-PropsPath', $props) $false
    $null = Invoke-VersionScript @('assert-not-lower', '0.2.0-beta.2', '-PropsPath', $props)
    $null = Invoke-VersionScript @('assert-not-lower', '0.2.0', '-PropsPath', $props)
    $null = Invoke-VersionScript @('assert-not-lower', '0.2.0-alpha', '-PropsPath', $props) $false

    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>9.9.9</Version></PropertyGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $tempRoot 'src\Sample\Sample.csproj') -Encoding utf8NoBOM
    $null = Invoke-VersionScript @('check', '-PropsPath', $props) $false

    & $versionScript check | Write-Output
    Write-Output "Version script tests passed."
}
finally {
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = (Resolve-Path -LiteralPath $tempRoot).Path
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedTemp.StartsWith($tempBase + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected version fixture directory.' }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

# Expected negative child-process tests must not leak their exit code to Actions' pwsh wrapper.
exit 0
