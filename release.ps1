# Build ProximityGrab.dll in Release, tag the current commit, and publish a GitHub release with the DLL attached.
#
# Usage:  bump <Version> in ProximityGrab\ProximityGrab.csproj, commit, then
#   .\release.ps1              # real release
#   .\release.ps1 -DryRun      # build + show what would happen
#   .\release.ps1 -Notes "text"  # custom notes instead of auto-generated
#
# One-time setup: winget install GitHub.cli ; gh auth login
param(
    [switch]$DryRun,
    [string]$Notes
)
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
