[CmdletBinding()]
param(
    [Parameter(Position = 0, Mandatory = $true)]
    [ValidateSet("print", "check", "set", "assert-not-lower")]
    [string]$Action,

    [Parameter(Position = 1)]
    [Alias("TargetVersion")]
    [string]$Version,

    [string]$PropsPath = (Join-Path $PSScriptRoot "..\Directory.Build.props"),

    [string]$AssemblyPath,

    [string]$ExpectedVersion
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$semVerPattern = '^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'

function Parse-SemVer([string]$Value) {
    $match = [regex]::Match($Value, $semVerPattern)
    if (-not $match.Success) {
        throw "Invalid SemVer '$Value'. Expected x.y.z with an optional prerelease/build suffix."
    }
    foreach ($identifier in ($match.Groups[4].Value -split '\.')) {
        if ($identifier -match '^0\d+$') {
            throw "Numeric prerelease identifiers must not have leading zeros: '$Value'."
        }
    }

    [pscustomobject]@{
        Major = [int64]$match.Groups[1].Value
        Minor = [int64]$match.Groups[2].Value
        Patch = [int64]$match.Groups[3].Value
        Prerelease = $match.Groups[4].Value
        Original = $Value
    }
}

function Compare-Identifier([string]$Left, [string]$Right) {
    $leftNumeric = $Left -match '^\d+$'
    $rightNumeric = $Right -match '^\d+$'
    if ($leftNumeric -and $rightNumeric) {
        return ([System.Numerics.BigInteger]::Parse($Left)).CompareTo([System.Numerics.BigInteger]::Parse($Right))
    }
    if ($leftNumeric) { return -1 }
    if ($rightNumeric) { return 1 }
    return [string]::CompareOrdinal($Left, $Right)
}

function Compare-SemVer([string]$Left, [string]$Right) {
    $leftVersion = Parse-SemVer $Left
    $rightVersion = Parse-SemVer $Right
    foreach ($property in @('Major', 'Minor', 'Patch')) {
        $comparison = $leftVersion.$property.CompareTo($rightVersion.$property)
        if ($comparison -ne 0) { return $comparison }
    }

    if (-not $leftVersion.Prerelease -and -not $rightVersion.Prerelease) { return 0 }
    if (-not $leftVersion.Prerelease) { return 1 }
    if (-not $rightVersion.Prerelease) { return -1 }

    $leftParts = $leftVersion.Prerelease -split '\.'
    $rightParts = $rightVersion.Prerelease -split '\.'
    for ($index = 0; $index -lt [Math]::Min($leftParts.Count, $rightParts.Count); $index++) {
        $comparison = Compare-Identifier $leftParts[$index] $rightParts[$index]
        if ($comparison -ne 0) { return $comparison }
    }
    return $leftParts.Count.CompareTo($rightParts.Count)
}

function Read-ProjectVersion {
    if (-not (Test-Path -LiteralPath $PropsPath -PathType Leaf)) {
        throw "Version source not found: $PropsPath"
    }
    [xml]$document = Get-Content -LiteralPath $PropsPath -Raw
    $nodes = @($document.SelectNodes('/Project/PropertyGroup/Version'))
    if ($nodes.Count -ne 1 -or [string]::IsNullOrWhiteSpace($nodes[0].InnerText)) {
        throw "Directory.Build.props must contain exactly one non-empty <Version> element."
    }
    $value = $nodes[0].InnerText.Trim()
    $null = Parse-SemVer $value
    return $value
}

function Test-VersionConsistency([string]$CurrentVersion) {
    [xml]$source = Get-Content -LiteralPath $PropsPath -Raw
    if (@($source.SelectNodes('/Project/PropertyGroup/*[self::VersionPrefix or self::VersionSuffix or self::AssemblyVersion or self::FileVersion or self::InformationalVersion]')).Count -gt 0) {
        throw 'Assembly/File/Informational version must be derived from the sole Version source.'
    }
    $repoRoot = (Resolve-Path -LiteralPath (Join-Path (Split-Path -Parent $PropsPath) ".")).Path
    $projectFiles = @(Get-ChildItem -LiteralPath $repoRoot -Recurse -Filter '*.csproj' -File |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    $overrides = foreach ($projectFile in $projectFiles) {
        [xml]$project = Get-Content -LiteralPath $projectFile.FullName -Raw
        if (@($project.SelectNodes('/Project/PropertyGroup/*[self::Version or self::VersionPrefix or self::VersionSuffix or self::AssemblyVersion or self::FileVersion or self::InformationalVersion]')).Count -gt 0) { $projectFile.FullName }
    }
    if (@($overrides).Count -gt 0) {
        throw "Project-level product/assembly version overrides are not allowed: $($overrides -join ', ')"
    }

    if ($ExpectedVersion) {
        $null = Parse-SemVer $ExpectedVersion
    }

    if ($AssemblyPath) {
        if (-not (Test-Path -LiteralPath $AssemblyPath -PathType Leaf)) {
            throw "Assembly not found: $AssemblyPath"
        }
        $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $AssemblyPath)).ProductVersion
        $expectedAssemblyVersion = if ($ExpectedVersion) { $ExpectedVersion } else { $CurrentVersion }
        if ($productVersion -ne $expectedAssemblyVersion) {
            throw "Assembly informational version '$productVersion' does not match '$expectedAssemblyVersion'."
        }
        $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Resolve-Path -LiteralPath $AssemblyPath)).FileVersion
        $numericVersion = (($expectedAssemblyVersion -split '[-+]')[0]) + '.0'
        if ($fileVersion -ne $numericVersion) { throw "Assembly file version '$fileVersion' does not match '$numericVersion'." }
    }
}

$current = Read-ProjectVersion

switch ($Action) {
    "print" {
        $current
    }
    "check" {
        Test-VersionConsistency $current
        "Version sources are consistent: $current"
    }
    "set" {
        if ([string]::IsNullOrWhiteSpace($Version)) { throw "set requires a target version." }
        $null = Parse-SemVer $Version
        [xml]$document = Get-Content -LiteralPath $PropsPath -Raw
        $document.Project.PropertyGroup.Version = $Version
        $settings = [Xml.XmlWriterSettings]::new()
        $settings.Indent = $true
        $settings.IndentChars = "  "
        $settings.NewLineChars = "`n"
        $settings.NewLineHandling = [Xml.NewLineHandling]::Replace
        $settings.Encoding = [Text.UTF8Encoding]::new($false)
        $writer = [Xml.XmlWriter]::Create($PropsPath, $settings)
        try { $document.Save($writer) } finally { $writer.Dispose() }
        "Version set to $Version"
    }
    "assert-not-lower" {
        if ([string]::IsNullOrWhiteSpace($Version)) { throw "assert-not-lower requires a target version." }
        $null = Parse-SemVer $Version
        if ((Compare-SemVer $Version $current) -lt 0) {
            throw "Target version '$Version' is lower than current version '$current'."
        }
        "Target version $Version is not lower than $current"
    }
}
