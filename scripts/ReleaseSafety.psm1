Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function Invoke-CheckedGit {
    param([string]$RepositoryRoot, [string[]]$Arguments)
    $result = @(& git -C $RepositoryRoot @Arguments)
    if ($LASTEXITCODE -ne 0) { throw "Git failed ($LASTEXITCODE): $($Arguments -join ' ')" }
    return $result
}

function Assert-ReleaseTagAbsent {
    param([string]$RepositoryRoot, [string]$Tag)
    $local = @(Invoke-CheckedGit $RepositoryRoot @('tag', '--list', $Tag))
    if ($local.Count -gt 0) { throw "Local Tag $Tag already exists; refusing to overwrite." }
    $remote = @(& git -C $RepositoryRoot ls-remote --exit-code --tags origin "refs/tags/$Tag")
    $result = $LASTEXITCODE
    if ($result -eq 0 -or $remote.Count -gt 0) { throw "Remote Tag $Tag already exists; refusing to overwrite." }
    if ($result -ne 2) { throw "Remote Tag lookup failed ($result); release refused." }
}

function Assert-ReleaseBranchUnchanged {
    param([string]$RepositoryRoot, [string]$DefaultBranch, [string]$BaseSha)
    $null = Invoke-CheckedGit $RepositoryRoot @('fetch', 'origin', "refs/heads/${DefaultBranch}:refs/remotes/origin/$DefaultBranch")
    $remoteSha = (Invoke-CheckedGit $RepositoryRoot @('rev-parse', "refs/remotes/origin/$DefaultBranch")) -join ''
    if ($remoteSha.Trim() -ne $BaseSha) {
        throw '默认分支在构建期间已更新，请从最新代码重新运行 Release。'
    }
    $head = (Invoke-CheckedGit $RepositoryRoot @('rev-parse', 'HEAD')) -join ''
    if ($head.Trim() -ne $BaseSha) { throw 'Local HEAD changed during release preparation.' }
}

function Assert-ReleaseFileAllowlist {
    param([string]$RepositoryRoot, [ValidateSet('stable', 'prerelease')] [string]$Channel, [string]$CurrentVersion, [string]$TargetVersion)
    $allowed = @('Directory.Build.props', 'CHANGELOG.md')
    $required = @('CHANGELOG.md')
    if ($Channel -eq 'stable') { $allowed += 'README.md'; $required += 'README.md' }
    if ($CurrentVersion -ne $TargetVersion) { $required += 'Directory.Build.props' }
    $changed = @(Invoke-CheckedGit $RepositoryRoot @('diff', '--name-only', 'HEAD'))
    $untracked = @(Invoke-CheckedGit $RepositoryRoot @('ls-files', '--others', '--exclude-standard'))
    if ($untracked.Count -gt 0) { throw "Unexpected untracked release files: $($untracked -join ', ')" }
    $unexpected = @($changed | Where-Object { $_ -notin $allowed })
    if ($unexpected.Count -gt 0) { throw "Unexpected release file changes: $($unexpected -join ', ')" }
    $deleted = @(Invoke-CheckedGit $RepositoryRoot @('diff', '--name-only', '--diff-filter=D', 'HEAD'))
    if ($deleted.Count -gt 0) { throw 'Release must not delete files.' }
    foreach ($file in $required) {
        if ($file -notin $changed) { throw "Missing required release change: $file" }
    }
    [xml]$props = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'Directory.Build.props') -Raw
    if ($props.SelectSingleNode('/Project/PropertyGroup/Version').InnerText -ne $TargetVersion) { throw 'Release source version mismatch.' }
    $changelog = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'CHANGELOG.md') -Raw
    if ($changelog -notmatch "(?m)^## \[v$([regex]::Escape($TargetVersion))\](?:\s|$)") { throw 'CHANGELOG lacks the target release entry.' }
    if ($Channel -eq 'stable') {
        $readme = Get-Content -LiteralPath (Join-Path $RepositoryRoot 'README.md') -Raw
        if ($readme -notmatch "(?s)<!-- release-readme:current-version:start -->.*?\[v$([regex]::Escape($TargetVersion))\].*?<!-- release-readme:current-version:end -->") { throw 'Stable README version mismatch.' }
    }
    $null = Invoke-CheckedGit $RepositoryRoot @('diff', '--check')
}

Export-ModuleMember -Function Invoke-CheckedGit, Assert-ReleaseTagAbsent, Assert-ReleaseBranchUnchanged, Assert-ReleaseFileAllowlist
