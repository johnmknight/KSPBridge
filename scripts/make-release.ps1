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

# Regenerate the KSP-AVC .version file from csproj so VERSION,
# KSP-AVC, and the assembly version are guaranteed to agree.
# KSP-AVC (the in-game updater) and CKAN both consume this file;
# the URL field below is what KSP-AVC fetches to check for updates,
# so the file MUST be committed to master for the URL to resolve.
# We treat the in-tree copy as build output: regenerate every
# release, commit the result alongside the version bump.
$versionJsonPath = Join-Path $repo 'GameData\KSPBridge\KSPBridge.version'
$semverParts = $version.Split('.')
if ($semverParts.Count -lt 3) {
    throw "Version '$version' is not in MAJOR.MINOR.PATCH form"
}
$versionJson = @"
{
  "NAME": "KSPBridge",
  "URL": "https://raw.githubusercontent.com/johnmknight/KSPBridge/master/GameData/KSPBridge/KSPBridge.version",
  "DOWNLOAD": "https://github.com/johnmknight/KSPBridge/releases",
  "GITHUB": {
    "USERNAME": "johnmknight",
    "REPOSITORY": "KSPBridge",
    "ALLOW_PRE_RELEASE": false
  },
  "VERSION": {
    "MAJOR": $($semverParts[0]),
    "MINOR": $($semverParts[1]),
    "PATCH": $($semverParts[2]),
    "BUILD": 0
  },
  "KSP_VERSION": {
    "MAJOR": 1,
    "MINOR": 12,
    "PATCH": 5
  },
  "KSP_VERSION_MIN": {
    "MAJOR": 1,
    "MINOR": 12,
    "PATCH": 0
  },
  "KSP_VERSION_MAX": {
    "MAJOR": 1,
    "MINOR": 12,
    "PATCH": 5
  }
}
"@
# Use UTF-8 without BOM — KSP-AVC has historically been finicky
# about UTF-8 BOM in version files, and CKAN's parser is strict
# JSON which trips on the BOM byte sequence.
[System.IO.File]::WriteAllText($versionJsonPath, $versionJson, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Regenerated KSPBridge.version for v$version"

# If the regen actually changed the file vs what's committed, auto-stage
# and commit just that file so the build below embeds the right
# SourceLink hash and the user does not have to remember a separate
# "git add + git commit" step after running this script.
#
# We commit ONLY the .version file (path-scoped commit) so any other
# unrelated working-tree changes the user has in flight are NOT
# accidentally swept up.
$git = (Get-Command git -ErrorAction SilentlyContinue).Source
if (-not $git -and (Test-Path 'C:\Program Files\Git\bin\git.exe')) {
    $git = 'C:\Program Files\Git\bin\git.exe'
}
if ($git) {
    $diffArgs = @('-C', $repo, 'diff', '--quiet', '--', 'GameData/KSPBridge/KSPBridge.version')
    $diffInfo = New-Object System.Diagnostics.ProcessStartInfo
    $diffInfo.FileName = $git
    $diffInfo.Arguments = ($diffArgs -join ' ')
    $diffInfo.UseShellExecute = $false
    $diffInfo.RedirectStandardOutput = $true
    $diffInfo.RedirectStandardError = $true
    $diffProc = [System.Diagnostics.Process]::Start($diffInfo)
    $diffProc.WaitForExit()
    $regenChanged = ($diffProc.ExitCode -ne 0)

    if ($regenChanged) {
        Write-Host "  KSPBridge.version content differs from HEAD; committing the regen." -ForegroundColor Yellow

        $addInfo = New-Object System.Diagnostics.ProcessStartInfo
        $addInfo.FileName = $git
        $addInfo.Arguments = "-C `"$repo`" add GameData/KSPBridge/KSPBridge.version"
        $addInfo.UseShellExecute = $false
        $addInfo.RedirectStandardOutput = $true
        $addInfo.RedirectStandardError = $true
        $addProc = [System.Diagnostics.Process]::Start($addInfo)
        $addProc.WaitForExit()
        if ($addProc.ExitCode -ne 0) {
            Write-Host "  git add failed (exit $($addProc.ExitCode)); regen left staged for manual commit." -ForegroundColor Red
        } else {
            $msg = "release-tooling: regenerate KSPBridge.version for v$version"
            $commitInfo = New-Object System.Diagnostics.ProcessStartInfo
            $commitInfo.FileName = $git
            $commitInfo.Arguments = "-C `"$repo`" commit -m `"$msg`" -- GameData/KSPBridge/KSPBridge.version"
            $commitInfo.UseShellExecute = $false
            $commitInfo.RedirectStandardOutput = $true
            $commitInfo.RedirectStandardError = $true
            $commitProc = [System.Diagnostics.Process]::Start($commitInfo)
            $commitOut = $commitProc.StandardOutput.ReadToEnd()
            $commitErr = $commitProc.StandardError.ReadToEnd()
            $commitProc.WaitForExit()
            if ($commitProc.ExitCode -eq 0) {
                Write-Host "  Committed: $msg" -ForegroundColor Green
                Write-Host "  Remember to 'git push' before tagging the release." -ForegroundColor Yellow
            } else {
                Write-Host "  git commit failed (exit $($commitProc.ExitCode))." -ForegroundColor Red
                if ($commitOut) { Write-Host "    $commitOut" -ForegroundColor DarkGray }
                if ($commitErr) { Write-Host "    $commitErr" -ForegroundColor DarkGray }
                Write-Host "  Regen is staged - finish the commit manually before tagging." -ForegroundColor Yellow
            }
        }
    }
} else {
    Write-Host "  git not on PATH; skipping auto-commit of KSPBridge.version regen." -ForegroundColor Yellow
    Write-Host "  Remember to 'git add GameData/KSPBridge/KSPBridge.version && git commit'." -ForegroundColor Yellow
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

# KSP-AVC version file sits at the mod folder root. KSP-AVC (the
# in-game add-on version checker) and CKAN both look for
# GameData/<ModName>/<ModName>.version and read its KSP_VERSION /
# KSP_VERSION_MIN / KSP_VERSION_MAX block to decide whether the
# mod is compatible with the user's KSP install. We just
# regenerated this file from csproj above so it's guaranteed
# to match the assembly version.
Copy-Item (Join-Path $repo 'GameData\KSPBridge\KSPBridge.version') $ksprModDir

# LICENSE + third-party notices live INSIDE GameData/KSPBridge/.
# This is the KSP-mod-standard placement so the user's KSP install
# stays uncluttered after extraction.
Copy-Item (Join-Path $repo 'LICENSE') $ksprModDir
Copy-Item (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $ksprModDir

# Ship the install verification script alongside the plugin so a
# user who just extracted the zip can run install-check.bat and
# get pass/warn/fail readouts for every prerequisite (KSP install,
# DLLs deployed, Settings.cfg fields, broker reachable, MQTT
# round-trip, WebSocket listener, console assets, web server).
# Documented in the README's "Verifying it works" section.
Copy-Item (Join-Path $repo 'scripts\install-check.ps1') $ksprModDir
Copy-Item (Join-Path $repo 'scripts\install-check.bat') $ksprModDir

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
