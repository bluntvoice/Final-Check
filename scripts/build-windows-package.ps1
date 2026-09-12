[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Test", "Prerelease", "Stable")]
    [string]$PackageKind,

    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string]$ReleaseNotesPath,

    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$versionScript = Join-Path $PSScriptRoot "version.ps1"
$desktopProject = Join-Path $repoRoot "src\FinalCheck.Desktop\FinalCheck.Desktop.csproj"
$iconPath = Join-Path $repoRoot "src\FinalCheck.App\Assets\avalonia-logo.ico"
$resolvedOutput = [IO.Path]::GetFullPath($OutputDirectory, $repoRoot)
$workRoot = Join-Path ([IO.Path]::GetTempPath()) ("finalcheck-package-" + [guid]::NewGuid().ToString('N'))
$publishDirectory = Join-Path $workRoot "publish"
$velopackDirectory = Join-Path $workRoot "velopack"

if (-not $IsWindows) {
    throw "Windows packaging must run on Windows."
}
if ($PSVersionTable.PSVersion.Major -lt 7) {
    throw "Windows packaging requires PowerShell 7 or later."
}
if (Test-Path -LiteralPath $resolvedOutput) {
    if (@(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -gt 0) {
        throw "Output directory must be empty: $resolvedOutput"
    }
}
else {
    New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null
}

$channel = switch ($PackageKind) {
    "Test" { "test" }
    "Prerelease" { "beta" }
    "Stable" { "win" }
}
switch ($PackageKind) {
    "Test" {
        if ($Version -notmatch '^\d+\.\d+\.\d+-dev\.\d+(?:\.\d+)?$') { throw "Test versions must use x.y.z-dev.run[.attempt]." }
    }
    "Prerelease" {
        if ($Version -notmatch '^\d+\.\d+\.\d+-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$' -or $Version -match '-dev\.') { throw "Prerelease requires a non-internal SemVer suffix." }
    }
    "Stable" {
        if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Stable versions must use x.y.z." }
    }
}
if ($PackageKind -ne 'Test' -and (& $versionScript print) -ne $Version) {
    throw "Formal package version must equal Directory.Build.props."
}

try {
    New-Item -ItemType Directory -Path $publishDirectory, $velopackDirectory -Force | Out-Null

    & $versionScript assert-not-lower $Version | Write-Output
    if (-not $SkipRestore) {
        & $dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed." }
        & $dotnet restore $desktopProject --runtime win-x64 -p:Version=$Version
        if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed." }
    }

    & $dotnet publish $desktopProject `
        --configuration Release `
        --runtime win-x64 `
        --self-contained true `
        --output $publishDirectory `
        --no-restore `
        -p:Version=$Version `
        -p:InformationalVersion=$Version `
        -p:DebugSymbols=false `
        -p:DebugType=None
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

    $appAssembly = Join-Path $publishDirectory "FinalCheck.App.dll"
    & $versionScript check -AssemblyPath $appAssembly -ExpectedVersion $Version | Write-Output
    $privateFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File -Recurse -Force | Where-Object {
        $_.Name -match '^(\.env(?:\..*)?|secrets\..*|finalcheck\.db.*)$' -or $_.Extension -in @('.db', '.sqlite', '.sqlite3')
    })
    if ($privateFiles.Count -gt 0) { throw "Publish contains private runtime data: $($privateFiles.Name -join ', ')" }

    $notesPath = $ReleaseNotesPath
    if ([string]::IsNullOrWhiteSpace($notesPath)) {
        $notesPath = Join-Path $workRoot "release-notes.md"
        [IO.File]::WriteAllText(
            $notesPath,
            "Internal $PackageKind package for Final Check $Version.",
            [Text.UTF8Encoding]::new($false))
    }
    elseif (-not (Test-Path -LiteralPath $notesPath -PathType Leaf)) {
        throw "Release notes file not found: $notesPath"
    }

    $vpkArguments = @(
        "tool", "run", "vpk", "--", "pack",
        "--outputDir", $velopackDirectory,
        "--channel", $channel,
        "--runtime", "win-x64",
        "--packId", "FinalCheck.App",
        "--packVersion", $Version,
        "--packDir", $publishDirectory,
        "--packAuthors", "bluntvoice",
        "--packTitle", "Final Check",
        "--mainExe", "FinalCheck.Desktop.exe",
        "--icon", $iconPath,
        "--releaseNotes", $notesPath,
        "--delta", "None",
        "--yes"
    )
    & $dotnet @vpkArguments
    if ($LASTEXITCODE -ne 0) { throw "Velopack packaging failed." }

    $setup = @(Get-ChildItem -LiteralPath $velopackDirectory -File -Filter '*Setup.exe')
    $portable = @(Get-ChildItem -LiteralPath $velopackDirectory -File -Filter '*Portable.zip')
    $fullPackage = @(Get-ChildItem -LiteralPath $velopackDirectory -File -Filter '*-full.nupkg')
    $releaseMetadata = @(Get-ChildItem -LiteralPath $velopackDirectory -File -Filter "releases.$channel.json")
    $assetMetadata = @(Get-ChildItem -LiteralPath $velopackDirectory -File -Filter "assets.$channel.json")
    foreach ($expectation in @(
        @{ Name = 'Setup.exe'; Files = $setup },
        @{ Name = 'Portable.zip'; Files = $portable },
        @{ Name = 'full.nupkg'; Files = $fullPackage },
        @{ Name = "releases.$channel.json"; Files = $releaseMetadata },
        @{ Name = "assets.$channel.json"; Files = $assetMetadata }
    )) {
        if ($expectation.Files.Count -ne 1 -or $expectation.Files[0].Length -le 0) {
            throw "Expected exactly one non-empty $($expectation.Name), found $($expectation.Files.Count)."
        }
    }

    $friendlyPrefix = "FinalCheck-v$Version-win-x64"
    $friendlySetup = Join-Path $resolvedOutput "$friendlyPrefix-Setup.exe"
    $friendlyPortable = Join-Path $resolvedOutput "$friendlyPrefix-Portable.zip"
    Copy-Item -LiteralPath $setup[0].FullName -Destination $friendlySetup
    Copy-Item -LiteralPath $portable[0].FullName -Destination $friendlyPortable

    foreach ($updateFile in @($fullPackage[0], $releaseMetadata[0])) {
        Copy-Item -LiteralPath $updateFile.FullName -Destination (Join-Path $resolvedOutput $updateFile.Name)
    }
    # releases.<channel>.json and the nupkg remain unchanged. The upload-only asset list
    # must refer to the safely renamed user-facing installer and portable files.
    $assetList = @(Get-Content -LiteralPath $assetMetadata[0].FullName -Raw | ConvertFrom-Json)
    foreach ($asset in $assetList) {
        if ($asset.Type -eq 'Installer') { $asset.RelativeFileName = [IO.Path]::GetFileName($friendlySetup) }
        if ($asset.Type -eq 'Portable') { $asset.RelativeFileName = [IO.Path]::GetFileName($friendlyPortable) }
    }
    [IO.File]::WriteAllText((Join-Path $resolvedOutput $assetMetadata[0].Name), (ConvertTo-Json -InputObject $assetList -Depth 10), [Text.UTF8Encoding]::new($false))

    $publishSize = (Get-ChildItem -LiteralPath $publishDirectory -File -Recurse | Measure-Object -Property Length -Sum).Sum
    $installedSize = $publishSize
    $metrics = [ordered]@{
        version = $Version
        packageKind = $PackageKind
        channel = $channel
        runtime = "win-x64"
        selfContained = $true
        publishBytes = [int64]$publishSize
        setupBytes = [int64](Get-Item -LiteralPath $friendlySetup).Length
        portableBytes = [int64](Get-Item -LiteralPath $friendlyPortable).Length
        estimatedInstalledBytes = [int64]$installedSize
    }
    $metricsPath = Join-Path $resolvedOutput "$friendlyPrefix-package-metrics.json"
    [IO.File]::WriteAllText($metricsPath, ($metrics | ConvertTo-Json), [Text.UTF8Encoding]::new($false))

    $checksumPath = Join-Path $resolvedOutput "$friendlyPrefix-SHA256.txt"
    $filesToHash = @(Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse | Where-Object { $_.FullName -ne $checksumPath } | Sort-Object FullName)
    $checksumLines = foreach ($file in $filesToHash) {
        $relativePath = [IO.Path]::GetRelativePath($resolvedOutput, $file.FullName).Replace('\', '/')
        "{0}  {1}" -f (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $relativePath
    }
    [IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.UTF8Encoding]::new($false))

    foreach ($line in Get-Content -LiteralPath $checksumPath) {
        if ($line -notmatch '^([0-9a-f]{64})  (.+)$') { throw "Invalid checksum line: $line" }
        $filePath = Join-Path $resolvedOutput ($Matches[2].Replace('/', '\'))
        $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actualHash -ne $Matches[1]) { throw "Checksum verification failed for $($Matches[2])." }
    }
    & (Join-Path $PSScriptRoot 'verify-windows-package.ps1') -Directory $resolvedOutput -Version $Version -PackageKind $PackageKind

    if ($env:GITHUB_OUTPUT) {
        "version=$Version" >> $env:GITHUB_OUTPUT
        "channel=$channel" >> $env:GITHUB_OUTPUT
        "artifact_directory=$resolvedOutput" >> $env:GITHUB_OUTPUT
        "installer=$friendlySetup" >> $env:GITHUB_OUTPUT
        "portable=$friendlyPortable" >> $env:GITHUB_OUTPUT
        "checksum=$checksumPath" >> $env:GITHUB_OUTPUT
        "metrics=$metricsPath" >> $env:GITHUB_OUTPUT
    }

    Write-Output "Package output: $resolvedOutput"
    Write-Output ("Publish: {0:N2} MiB; Setup: {1:N2} MiB; Portable: {2:N2} MiB" -f `
        ($metrics.publishBytes / 1MB), ($metrics.setupBytes / 1MB), ($metrics.portableBytes / 1MB))
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        $resolvedWorkRoot = (Resolve-Path -LiteralPath $workRoot).Path
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedWorkRoot.StartsWith($tempBase + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to remove unexpected work directory: $resolvedWorkRoot"
        }
        Remove-Item -LiteralPath $resolvedWorkRoot -Recurse -Force
    }
}
