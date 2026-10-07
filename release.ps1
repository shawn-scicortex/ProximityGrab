<#
.SYNOPSIS
Build ProximityGrab.dll, tag the current commit, and publish a GitHub release with the DLL attached.

.DESCRIPTION
Workflow: bump <Version> in ProximityGrab\ProximityGrab.csproj, commit, then run with -Release.
Run with no options to show this help. Requires the GitHub CLI (gh auth login).

.PARAMETER Release
Actually do the release: build, tag v<Version>, push, create the GitHub release.

.PARAMETER DryRun
Build and show what would happen, without tagging, pushing or releasing.

.PARAMETER Notes
Custom release notes instead of auto-generated ones.

.EXAMPLE
.\release.ps1 -Release

.EXAMPLE
.\release.ps1 -DryRun

.EXAMPLE
.\release.ps1 -Release -Notes "Fixes grab miss effect"
#>
param(
    [switch]$Release,
    [switch]$DryRun,
    [string]$Notes
)
if (-not ($Release -or $DryRun)) {
    Get-Help $PSCommandPath -Detailed
    return
}
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$repo = 'shawn-scicortex/ProximityGrab'
$csproj = 'ProximityGrab\ProximityGrab.csproj'
$version = ([xml](Get-Content $csproj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "No <Version> found in $csproj" }
$tag = "v$version"

if (-not $DryRun) {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw 'gh not found. Run: winget install GitHub.cli ; gh auth login' }
    if (git status --porcelain --untracked-files=no) { throw 'Uncommitted changes to tracked files. Commit them first.' }
    if (git tag -l $tag) { throw "Tag $tag already exists. Bump <Version> in $csproj." }
}

dotnet build $csproj -c Release
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$dll = Join-Path $PSScriptRoot 'ProximityGrab\bin\Release\net10.0\ProximityGrab.dll'
if (-not (Test-Path $dll)) { throw "Missing $dll" }

if ($DryRun) {
    Write-Host "DRY RUN: would tag $tag, push, and release $dll to $repo"
    return
}

git tag $tag
git push origin HEAD $tag
if ($LASTEXITCODE -ne 0) { throw 'Push failed.' }

$ghArgs = @('release', 'create', $tag, $dll, '-R', $repo, '--title', "ProximityGrab $version")
if ($Notes) { $ghArgs += @('--notes', $Notes) } else { $ghArgs += '--generate-notes' }
gh @ghArgs
