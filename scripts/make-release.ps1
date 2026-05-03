<#
.SYNOPSIS
    Produces a KSP-mod-standard release zip from a clean Release build.

.DESCRIPTION
    Builds the plugin in Release configuration, then assembles a
    distribution zip whose internal layout matches the standard KSP
    mod packaging convention used across Spacedock and CKAN:

        KSPBridge-v<version>.zip
        +-- GameData/
            +-- KSPBridge/
                +-- Plugins/
                |   +-- KSPBridge.dll
                |   +-- MQTTnet.dll
                |   +-- MQTTnet.Extensions.ManagedClient.dll
                +-- Settings.cfg
                +-- LICENSE
                +-- THIRD-PARTY-NOTICES.md

    Notably, LICENSE and the third-party notices live inside
    GameData/KSPBridge/ rather than at the zip root. This keeps the
    user's KSP install clean: when they extract the zip into their
    KSP root, only the GameData/ tree is touched and the licenses
    travel with the mod folder. (Spacedock and CKAN expect this
    layout — files at the zip root would otherwise litter the user's
    KSP install when they extract.)

    The version is read from src/KSPBridge/KSPBridge.csproj's
    <Version> property so the zip filename and the assembly version
    can never drift out of sync.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release; the script does not
    support producing a Debug release zip on purpose.

.OUTPUTS
    Writes _release/KSPBridge-v<version>.zip and prints its path
    plus contents. Both _release/ and _release_stage/ are
    .gitignored so neither pollutes the working tree.

.EXAMPLE
    pwsh scripts/make-release.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Resolve the repo root from the script's own location so the script
# works regardless of the caller's current directory.
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$csproj = Join-Path $repo 'src\KSPBridge\KSPBridge.csproj'
$bin = Join-Path $repo "src\KSPBridge\bin\$Configuration"
$stage = Join-Path $repo '_release_stage'
$releaseDir = Join-Path $repo '_release'

# Pull the version from csproj. Single source of truth — no second
# constant to forget when bumping.
[xml]$csprojXml = Get-Content $csproj
$version = ($csprojXml.Project.PropertyGroup |
    Where-Object { $_.Version }).Version
if (-not $version) {
    throw "Could not read <Version> from $csproj"
}

# Locate dotnet. Prefer PATH; fall back to the canonical install path.
# Some launching contexts (notably automation that spawns a fresh
# powershell.exe with a stripped PATH) won't have dotnet on PATH even
# though it's installed system-wide.
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet -and (Test-Path 'C:\Program Files\dotnet\dotnet.exe')) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
}
if (-not $dotnet) {
    throw 'dotnet SDK not found. Install from https://dotnet.microsoft.com/download.'
}

Write-Host "Building $Configuration with $dotnet..."

# Use System.Diagnostics.Process directly so stdout/stderr capture is
# reliable across spawning contexts. PowerShell's call-operator
# invocation can lose output and set $LASTEXITCODE inconsistently when
# the parent shell is itself a redirect target.
$pinfo = New-Object System.Diagnostics.ProcessStartInfo
$pinfo.FileName = $dotnet
$pinfo.Arguments = "build `"$repo\KSPBridge.sln`" -c $Configuration --nologo"
$pinfo.RedirectStandardOutput = $true
$pinfo.RedirectStandardError = $true
$pinfo.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($pinfo)
$buildOut = $proc.StandardOutput.ReadToEnd()
$buildErr = $proc.StandardError.ReadToEnd()
$proc.WaitForExit()
if ($buildOut) { Write-Host $buildOut }
if ($buildErr) { Write-Host $buildErr }
if ($proc.ExitCode -ne 0) {
    throw "Build failed (exit $($proc.ExitCode))"
}

# Verify the build outputs we expect to package are actually present.
# Otherwise downstream users get a half-broken zip and the failure
# only surfaces at game-launch time.
$required = @(
    'KSPBridge.dll',
    'MQTTnet.dll',
    'MQTTnet.Extensions.ManagedClient.dll'
)
foreach ($f in $required) {
    $p = Join-Path $bin $f
    if (-not (Test-Path $p)) {
        throw "Build output missing: $p"
    }
}

# Wipe and recreate the staging tree. We always start clean so a
# previously-broken layout can't leak into a new zip.
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$ksprModDir = Join-Path $stage 'GameData\KSPBridge'
$pluginsDir = Join-Path $ksprModDir 'Plugins'
New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null

# DLLs go in GameData/KSPBridge/Plugins/
foreach ($f in $required) {
    Copy-Item (Join-Path $bin $f) $pluginsDir
}

# Settings.cfg sits at the mod folder root — same convention as
# every other KSP mod that ships a default config.
Copy-Item (Join-Path $repo 'GameData\KSPBridge\Settings.cfg') $ksprModDir

# LICENSE + third-party notices live INSIDE GameData/KSPBridge/.
# This is the KSP-mod-standard placement so the user's KSP install
# stays uncluttered after extraction.
Copy-Item (Join-Path $repo 'LICENSE') $ksprModDir
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $ksprModDir

# Materialise the zip. Use Compress-Archive's content-of-each-path
# semantics: pass the staging dir's children explicitly so the
# staging dir's own name doesn't end up inside the archive.
if (-not (Test-Path $releaseDir)) {
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
}
$zipPath = Join-Path $releaseDir "KSPBridge-v$version.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
$items = Get-ChildItem $stage -Force | ForEach-Object { $_.FullName }
Compress-Archive -Path $items -DestinationPath $zipPath -Force

# Sanity-print: zip path, size, embedded plugin version, contents.
$dllInfo = (Get-Item (Join-Path $pluginsDir 'KSPBridge.dll')).VersionInfo
Write-Host ''
Write-Host ('Release zip:        ' + $zipPath)
Write-Host ('Size:               ' + (Get-Item $zipPath).Length + ' bytes')
Write-Host ('FileVersion:        ' + $dllInfo.FileVersion)
Write-Host ('ProductVersion:     ' + $dllInfo.ProductVersion)
Write-Host ''
Write-Host 'Contents:'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
$z.Entries | ForEach-Object { Write-Host ('  ' + $_.FullName + '  (' + $_.Length + ' bytes)') }
$z.Dispose()
