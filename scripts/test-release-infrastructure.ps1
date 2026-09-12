[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$sourceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('finalcheck-release-tests-' + [guid]::NewGuid().ToString('N'))
$repo = Join-Path $tempRoot 'client'
$origin = Join-Path $tempRoot 'origin.git'
$peer = Join-Path $tempRoot 'peer'
$savedGithubOutput = $env:GITHUB_OUTPUT
$env:GITHUB_OUTPUT = $null
Import-Module (Join-Path $PSScriptRoot 'ReleaseSafety.psm1') -Force

function Run-Script([string]$Name, [string[]]$Arguments, [bool]$Success = $true) {
    $output = @(& (Get-Process -Id $PID).Path -NoProfile -File (Join-Path $PSScriptRoot $Name) @Arguments 2>&1)
    if (($LASTEXITCODE -eq 0) -ne $Success) { throw "$Name unexpected exit code. $($output -join [Environment]::NewLine)" }
    return $output
}

function Expect-Failure([scriptblock]$Operation) {
    $failed = $false
    try { & $Operation | Out-Null } catch { $failed = $true }
    if (-not $failed) { throw 'Expected the safety guard to refuse the operation.' }
}

try {
    New-Item -ItemType Directory -Path $repo, $origin -Force | Out-Null
    $null = Invoke-CheckedGit $sourceRoot @('init', '--bare', '--initial-branch=main', $origin)
    $null = Invoke-CheckedGit $repo @('init', '--initial-branch=main')
    $null = Invoke-CheckedGit $repo @('config', 'user.name', 'Release fixture')
    $null = Invoke-CheckedGit $repo @('config', 'user.email', 'fixture@example.invalid')
    foreach ($file in @('Directory.Build.props', 'README.md', 'CHANGELOG.md')) {
        Copy-Item -LiteralPath (Join-Path $sourceRoot $file) -Destination (Join-Path $repo $file)
    }
    '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>' |
        Set-Content -LiteralPath (Join-Path $repo 'Sample.csproj') -Encoding utf8NoBOM
    $null = Run-Script 'version.ps1' @('set', '0.1.0-alpha.0', '-PropsPath', (Join-Path $repo 'Directory.Build.props'))
    $null = Invoke-CheckedGit $repo @('add', 'Directory.Build.props', 'README.md', 'CHANGELOG.md', 'Sample.csproj')
    $null = Invoke-CheckedGit $repo @('commit', '-m', 'fixture baseline')
    $null = Invoke-CheckedGit $repo @('remote', 'add', 'origin', $origin)
    $null = Invoke-CheckedGit $repo @('push', '--set-upstream', 'origin', 'main')
    $baseSha = (Invoke-CheckedGit $repo @('rev-parse', 'HEAD')) -join ''
    $notes = "- 中文第一项`n- Second item`n`n**多行 Markdown**"
    $outputDirectory = Join-Path $tempRoot 'request'
    $requestArgs = @('-Version', '0.1.0', '-Channel', 'stable', '-ReleaseNotes', $notes, '-ReadmeSummary', '稳定版摘要', '-DefaultBranch', 'main', '-RefName', 'main', '-BaseSha', $baseSha, '-OutputDirectory', $outputDirectory, '-RepositoryRoot', $repo)
    $null = Run-Script 'prepare-release.ps1' $requestArgs $false
    $confirmed = $requestArgs + @('-ConfirmRealRelease', 'true')
    $null = Run-Script 'prepare-release.ps1' $confirmed
    $notesPath = Join-Path $outputDirectory 'release-notes.md'
    $summaryPath = Join-Path $outputDirectory 'readme-summary.md'
    if ([IO.File]::ReadAllText($notesPath) -ne "## 版本亮点`n`n$notes`n") { throw 'UTF-8/multiline notes regression.' }
    foreach ($change in @(
        @{Parameter='-RefName'; Value='feature/unsafe'},
        @{Parameter='-Version'; Value='0.1.0-beta.1'},
        @{Parameter='-Version'; Value='0.0.9'},
        @{Parameter='-ReleaseNotes'; Value=''},
        @{Parameter='-ReadmeSummary'; Value=''}
    )) {
        $invalid = [string[]]$confirmed.Clone()
        $invalid[[Array]::IndexOf($invalid, $change.Parameter) + 1] = $change.Value
        $null = Run-Script 'prepare-release.ps1' $invalid $false
    }

    # Fixture-only tags live in an isolated local repository/bare remote, never Final Check.
    $null = Invoke-CheckedGit $repo @('tag', '-a', 'v0.0.9', '-m', 'recent fixture release')
    $null = Run-Script 'prepare-release.ps1' $confirmed $false
    $null = Run-Script 'prepare-release.ps1' ($confirmed + @('-AllowReleaseWithin24h', 'true'))
    $null = Invoke-CheckedGit $sourceRoot @('clone', '--branch', 'main', $origin, $peer)
    $null = Invoke-CheckedGit $peer @('config', 'user.name', 'Peer fixture')
    $null = Invoke-CheckedGit $peer @('config', 'user.email', 'peer@example.invalid')
    $null = Invoke-CheckedGit $peer @('tag', '-a', 'v0.1.0-beta.1', '-m', 'remote-only fixture tag')
    $null = Invoke-CheckedGit $peer @('push', 'origin', 'refs/tags/v0.1.0-beta.1')
    Expect-Failure { Assert-ReleaseTagAbsent $repo 'v0.1.0-beta.1' }
    $null = Invoke-CheckedGit $repo @('tag', '-a', 'v0.1.0', '-m', 'existing fixture tag')
    $null = Run-Script 'prepare-release.ps1' ($confirmed + @('-AllowReleaseWithin24h', 'true')) $false

    $originalReadme = [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
    $null = Run-Script 'version.ps1' @('set', '0.1.0-beta.2', '-PropsPath', (Join-Path $repo 'Directory.Build.props'))
    $null = Run-Script 'update-release-docs.ps1' @('-Version', '0.1.0-beta.2', '-Channel', 'prerelease', '-NotesPath', $notesPath, '-RepositoryRoot', $repo)
    if ([IO.File]::ReadAllText((Join-Path $repo 'README.md')) -ne $originalReadme) { throw 'Prerelease must not change stable README.' }
    Assert-ReleaseFileAllowlist $repo 'prerelease' '0.1.0-alpha.0' '0.1.0-beta.2'

    $null = Run-Script 'version.ps1' @('set', '0.1.0', '-PropsPath', (Join-Path $repo 'Directory.Build.props'))
    $null = Run-Script 'update-release-docs.ps1' @('-Version', '0.1.0', '-Channel', 'stable', '-NotesPath', $notesPath, '-ReadmeSummaryPath', $summaryPath, '-RepositoryRoot', $repo)
    $updatedReadme = [IO.File]::ReadAllText((Join-Path $repo 'README.md'))
    $changelogPath = Join-Path $repo 'CHANGELOG.md'
    $updatedChangelog = [IO.File]::ReadAllText($changelogPath)
    if (-not $updatedReadme.Contains('[v0.1.0]') -or -not $updatedReadme.Contains('稳定版摘要')) { throw 'Stable README update regression.' }
    if (-not $updatedChangelog.Contains('## [v0.1.0-beta.2]') -or -not $updatedChangelog.Contains('## [v0.1.0]') -or -not $updatedChangelog.Contains("### 版本亮点`n`n$notes") -or -not $updatedChangelog.Contains('## [Unreleased]')) { throw 'CHANGELOG history/newline regression.' }
    Assert-ReleaseFileAllowlist $repo 'stable' '0.1.0-alpha.0' '0.1.0'
    Expect-Failure { Assert-ReleaseFileAllowlist $repo 'prerelease' '0.1.0-alpha.0' '0.1.0' }
    $null = Run-Script 'update-release-docs.ps1' @('-Version', '0.1.0', '-Channel', 'stable', '-NotesPath', $notesPath, '-ReadmeSummaryPath', $summaryPath, '-RepositoryRoot', $repo) $false
    if ([IO.File]::ReadAllText($changelogPath) -ne $updatedChangelog) { throw 'Duplicate release must preserve CHANGELOG.' }

    Add-Content -LiteralPath (Join-Path $repo 'Sample.csproj') -Value '<!-- accidental code change -->' -Encoding utf8NoBOM
    Expect-Failure { Assert-ReleaseFileAllowlist $repo 'stable' '0.1.0-alpha.0' '0.1.0' }
    Assert-ReleaseBranchUnchanged $repo 'main' $baseSha
    Add-Content -LiteralPath (Join-Path $peer 'CHANGELOG.md') -Value 'Peer changed main.' -Encoding utf8NoBOM
    $null = Invoke-CheckedGit $peer @('add', 'CHANGELOG.md')
    $null = Invoke-CheckedGit $peer @('commit', '-m', 'peer changed default branch')
    $null = Invoke-CheckedGit $peer @('push', 'origin', 'main')
    Expect-Failure { Assert-ReleaseBranchUnchanged $repo 'main' $baseSha }

    # An existing remote tag must make an atomic branch+tag push leave BOTH refs unchanged.
    $null = Invoke-CheckedGit $peer @('tag', '-a', 'atomic-fixture-tag', '-m', 'remote atomic fixture')
    $null = Invoke-CheckedGit $peer @('push', 'origin', 'refs/tags/atomic-fixture-tag')
    $remoteTagBefore = (Invoke-CheckedGit $repo @('ls-remote', '--tags', 'origin', 'refs/tags/atomic-fixture-tag')) -join ''
    $null = Invoke-CheckedGit $repo @('tag', '-a', 'atomic-fixture-tag', '-m', 'different local atomic fixture')
    $null = & git -C $repo push --atomic origin 'HEAD:refs/heads/atomic-fixture-branch' 'refs/tags/atomic-fixture-tag' 2>&1
    if ($LASTEXITCODE -eq 0) { throw 'Expected conflicting atomic push to fail.' }
    $remoteBranch = @(& git -C $repo ls-remote --exit-code --heads origin refs/heads/atomic-fixture-branch)
    if ($LASTEXITCODE -ne 2 -or $remoteBranch.Count -gt 0) { throw 'Atomic failure unexpectedly created the branch.' }
    $remoteTagAfter = (Invoke-CheckedGit $repo @('ls-remote', '--tags', 'origin', 'refs/tags/atomic-fixture-tag')) -join ''
    if ($remoteTagAfter -ne $remoteTagBefore) { throw 'Atomic failure unexpectedly changed the tag.' }

    foreach ($file in Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -in @('.ps1', '.psm1')) {
        $parseErrors = $null
        $tokens = $null
        $null = [Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors.Count -gt 0) { throw "PowerShell parse errors: $($file.Name)" }
    }
    $testWorkflow = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/build-test.yml') -Raw
    if ($testWorkflow -notmatch 'contents: read' -or $testWorkflow -notmatch 'persist-credentials: false' -or $testWorkflow -notmatch 'retention-days: 14' -or $testWorkflow -notmatch 'if-no-files-found: error' -or $testWorkflow -match '(contents: write|git (commit|push|tag)|gh release)') { throw 'Test build isolation regression.' }
    $releaseWorkflow = Get-Content -LiteralPath (Join-Path $sourceRoot '.github/workflows/release.yml') -Raw
    foreach ($required in @('contents: write', 'cancel-in-progress: false', 'confirm_real_release:', 'allow_release_within_24h:', 'git push --atomic', 'git tag -a', 'Assert-ReleaseBranchUnchanged', 'Assert-ReleaseFileAllowlist', '--notes-file', '--prerelease', '--latest')) {
        if (-not $releaseWorkflow.Contains($required)) { throw "Release workflow safety regression: $required" }
    }
    if ($releaseWorkflow -match '(git .*--force|reset --hard)') { throw 'Destructive release Git command detected.' }
    Write-Output 'Release infrastructure tests passed: confirmation, channels, SemVer, local/remote tags, 24h, notes, docs, allowlist, branch race, atomic failure, script syntax, test isolation.'
}
finally {
    $env:GITHUB_OUTPUT = $savedGithubOutput
    if (Test-Path -LiteralPath $tempRoot) {
        $resolvedTemp = (Resolve-Path -LiteralPath $tempRoot).Path
        $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\')
        if (-not $resolvedTemp.StartsWith($tempBase + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected fixture cleanup path.' }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}
exit 0
