param(
    [switch]$Release,
    [switch]$DryRun,
    [string]$Notes
)
if (-not ($Release -or $DryRun)) {
    Write-Host @'
Build ProximityGrab.dll, tag v<Version>, and publish a GitHub release with the DLL.
Bump <Version> in ProximityGrab\ProximityGrab.csproj and commit first.

Options:
  -Release        do the release (build, tag, push, gh release create)
  -DryRun         build and show what would happen
  -Notes <text>   custom release notes (default: auto-generated)

Examples:
  .\release.ps1 -Release
  .\release.ps1 -DryRun
  .\release.ps1 -Release -Notes "Fixes grab miss effect"
'@
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
