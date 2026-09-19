<#Requires -Version 5.1>
<#
.SYNOPSIS
    Builds the ProximityGrab mod and zips the binary plus source into a release zip.

.DESCRIPTION
    Runs dotnet build (Release by default), stages ProximityGrab.dll alongside
    README.md and the src/ tree (csproj + .cs files) via an explicit file list,
    and compresses the result to dist/ProximityGrab-<version>.zip.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Version
    Release version used in the zip name. Defaults to the <Version> in
    ProximityGrab/ProximityGrab.csproj.

.PARAMETER OutputDir
    Directory for the release zip. Defaults to dist/ next to this script.

.EXAMPLE
    .\build.ps1
.EXAMPLE
    .\build.ps1 -Configuration Debug -Version 0.9.1-test
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$RepoRoot = $PSScriptRoot
$ProjectFile = Join-Path $RepoRoot "ProximityGrab\ProximityGrab.csproj"

if (-not (Test-Path -LiteralPath $ProjectFile)) {
    throw "Project file not found: $ProjectFile"
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$csproj = Get-Content -LiteralPath $ProjectFile
    $Version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($Version)) {
        throw "Could not determine <Version> from $ProjectFile; pass -Version explicitly."
    }
}

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $RepoRoot "dist"
}

$DllPath = Join-Path $RepoRoot "ProximityGrab\bin\$Configuration\net10.0\ProximityGrab.dll"
$ZipPath = Join-Path $OutputDir "ProximityGrab-$Version.zip"

$SourceFiles = @(
    "ProximityGrab\ProximityGrab.csproj",
    "ProximityGrab\Mod.cs",
    "ProximityGrab\FistGesture.cs",
    "ProximityGrab\Patches.cs",
    "ProximityGrab\PrecisionGrab.cs",
    "ProximityGrab\ProximityGrabState.cs"
)
$ReadmeFile = "README.md"

# 1. Build (release artifact only: no CopyToMods local install).
& dotnet build $ProjectFile -c $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $DllPath)) {
    throw "Expected DLL not found after build: $DllPath"
}

foreach ($relative in ($SourceFiles + @($ReadmeFile))) {
    $full = Join-Path $RepoRoot $relative
    if (-not (Test-Path -LiteralPath $full)) {
        throw "Expected release file not found: $full"
    }
}

# 2. Stage via an explicit file list so bin/, obj/, .git/ and logs can never leak in.
$StageRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ProximityGrab-release-" + [System.Guid]::NewGuid().ToString("N"))
try {
    New-Item -ItemType Directory -Path $StageRoot | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $StageRoot "src") | Out-Null

    Copy-Item -LiteralPath $DllPath -Destination $StageRoot
    Copy-Item -LiteralPath (Join-Path $RepoRoot $ReadmeFile) -Destination $StageRoot
    foreach ($relative in $SourceFiles) {
        Copy-Item -LiteralPath (Join-Path $RepoRoot $relative) -Destination (Join-Path $StageRoot "src")
    }

    # 3. Zip.
    if (-not (Test-Path -LiteralPath $OutputDir)) {
        New-Item -ItemType Directory -Path $OutputDir | Out-Null
    }
    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }
    Compress-Archive -Path (Join-Path $StageRoot "*") -DestinationPath $ZipPath

    Write-Output "Release zip: $ZipPath"
    Write-Output "Contents:"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        foreach ($entry in $archive.Entries) {
            Write-Output ("  " + $entry.FullName + " (" + $entry.Length + " bytes)")
        }
    }
    finally {
        $archive.Dispose()
    }
}
finally {
    if (Test-Path -LiteralPath $StageRoot) {
        Remove-Item -LiteralPath $StageRoot -Recurse -Force
    }
}
