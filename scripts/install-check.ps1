<#
.SYNOPSIS
    Verifies a KSPBridge install is wired up correctly.

.DESCRIPTION
    Walks every prerequisite a user needs in place before launching
    KSP and expecting telemetry to flow:

      1. KSP install detected
      2. Plugin DLLs deployed under GameData/KSPBridge/Plugins/
      3. KSPBridge.version present and parseable
      4. Settings.cfg present with all four required fields valid
      5. TCP socket open to broker_host:broker_port
      6. MQTT round-trip via mosquitto_pub + mosquitto_sub
      7. WebSocket listener reachable (for the FDO browser console)
      8. FDO console HTML files present
      9. Python on PATH (needed to serve the console)
     10. Spin up python -m http.server on a free port, fetch the
         console URL, confirm HTTP 200, then stop the server

    Read-only with respect to persistent state. The MQTT round-trip
    publishes a single message on a private sub-topic that does not
    collide with real telemetry. The web server check spins up a
    transient http.server on a randomly-allocated port and stops
    it after one fetch.

.PARAMETER KSPRoot
    Override the KSP install path. Defaults to the standard Steam
    install location.

.PARAMETER WebSocketPort
    Expected WebSocket listener port. Defaults to 9002, the
    convention used by the bundled FDO console. Settings.cfg only
    declares the MQTT TCP port; the WS port is a separate setting.

.OUTPUTS
    Exit code 0 if every required check passes, 1 otherwise.
    Warnings (e.g. WebSocket not configured, Python missing) do
    not fail the run.

.EXAMPLE
    pwsh scripts/install-check.ps1

.EXAMPLE
    pwsh scripts/install-check.ps1 -KSPRoot 'D:\Games\KSP' -WebSocketPort 9001
#>
[CmdletBinding()]
param(
    [string]$KSPRoot,
    [int]$WebSocketPort = 9002
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------
# Result tracking. Each check appends one entry; the summary at
# the end tallies passes / warns / fails and decides exit code.
# ---------------------------------------------------------------
$script:Results = @()

function Add-Result {
    param(
        [string]$Name,
        [ValidateSet('pass','warn','fail')] [string]$Status,
        [string]$Detail,
        [string]$Remedy
    )
    $script:Results += [PSCustomObject]@{
        Name = $Name; Status = $Status; Detail = $Detail; Remedy = $Remedy
    }

    $glyph = switch ($Status) { 'pass' {'[OK] '} 'warn' {'[!]  '} 'fail' {'[X]  '} }
    $color = switch ($Status) { 'pass' {'Green'} 'warn' {'Yellow'} 'fail' {'Red'} }
    Write-Host ("  $glyph$Name") -ForegroundColor $color
    if ($Detail) { Write-Host "       $Detail" -ForegroundColor DarkGray }
    if ($Status -ne 'pass' -and $Remedy) {
        $Remedy -split "`n" | ForEach-Object { Write-Host "       $_" -ForegroundColor Yellow }
    }
}

function Section { param([string]$Title)
    Write-Host ""
    Write-Host "=== $Title ===" -ForegroundColor Cyan
}

# ---------------------------------------------------------------
# 1. KSP install
# ---------------------------------------------------------------
Section '1. KSP install'

if (-not $KSPRoot) {
    $KSPRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program'
}
if (-not (Test-Path (Join-Path $KSPRoot 'KSP_x64.exe'))) {
    Add-Result 'KSP install located' 'fail' "KSP_x64.exe not found under $KSPRoot" `
        "Pass -KSPRoot 'D:\Path\To\KSP' if your install is elsewhere."
    Write-Host ""
    Write-Host "Aborting: cannot test a KSPBridge install without finding KSP first." -ForegroundColor Red
    exit 1
}
Add-Result 'KSP install located' 'pass' $KSPRoot

$modDir = Join-Path $KSPRoot 'GameData\KSPBridge'
$pluginsDir = Join-Path $modDir 'Plugins'

# ---------------------------------------------------------------
# 2. Plugin DLLs deployed
# ---------------------------------------------------------------
Section '2. Plugin deployment'

$requiredDlls = @(
    'KSPBridge.dll',
    'MQTTnet.dll',
    'MQTTnet.Extensions.ManagedClient.dll'
)
foreach ($dll in $requiredDlls) {
    $path = Join-Path $pluginsDir $dll
    if (Test-Path $path) {
        $vi = (Get-Item $path).VersionInfo
        $ver = if ($vi.FileVersion) { " (FileVersion $($vi.FileVersion))" } else { '' }
        Add-Result $dll 'pass' "$([int]((Get-Item $path).Length)) bytes$ver"
    } else {
        Add-Result $dll 'fail' "missing from $pluginsDir" `
            "Reinstall: extract KSPBridge-vX.Y.Z.zip into the KSP install root,
or rebuild from source (scripts\make-release.ps1) which auto-deploys."
    }
}

# ---------------------------------------------------------------
# 3. KSPBridge.version
# ---------------------------------------------------------------
Section '3. KSPBridge.version (KSP-AVC / CKAN compat)'

$verPath = Join-Path $modDir 'KSPBridge.version'
if (-not (Test-Path $verPath)) {
    Add-Result 'KSPBridge.version present' 'warn' "missing at $verPath" `
        "Functional impact: KSP-AVC users will not be notified of updates.
Releases from v0.15.0 onward include this file in the zip."
} else {
    try {
        $verJson = Get-Content $verPath -Raw | ConvertFrom-Json
        $v = $verJson.VERSION
        $verStr = "$($v.MAJOR).$($v.MINOR).$($v.PATCH)"
        $kMin = $verJson.KSP_VERSION_MIN
        $kMax = $verJson.KSP_VERSION_MAX
        Add-Result 'KSPBridge.version parses' 'pass' `
            "version $verStr, KSP $($kMin.MAJOR).$($kMin.MINOR).$($kMin.PATCH) - $($kMax.MAJOR).$($kMax.MINOR).$($kMax.PATCH)"
    } catch {
        Add-Result 'KSPBridge.version parses' 'fail' "JSON parse error: $($_.Exception.Message)" `
            'Re-extract the release zip - the file is corrupted.'
    }
}

# ---------------------------------------------------------------
# 4. Settings.cfg
# ---------------------------------------------------------------
Section '4. Settings.cfg'

$cfgPath = Join-Path $modDir 'Settings.cfg'
$brokerHost = $null
$brokerPort = $null
$topicPrefix = $null
$clientId = $null

if (-not (Test-Path $cfgPath)) {
    Add-Result 'Settings.cfg present' 'fail' "missing at $cfgPath" `
        'Re-extract the release zip - Settings.cfg ships at the mod folder root.'
} else {
    Add-Result 'Settings.cfg present' 'pass' $cfgPath

    # Minimal ConfigNode parse - avoid pulling in KSP assemblies.
    # Field names match the keys Settings.cs looks up.
    $cfgLines = Get-Content $cfgPath
    foreach ($line in $cfgLines) {
        $trim = $line.Trim()
        if ($trim -eq '' -or $trim.StartsWith('//')) { continue }
        if ($trim -match '^\s*(\w+)\s*=\s*(.+?)\s*$') {
            $k = $matches[1]; $v = $matches[2]
            switch ($k) {
                'broker_host'   { $brokerHost = $v }
                'broker_port'   { $brokerPort = $v }
                'topic_prefix'  { $topicPrefix = $v }
                'client_id'     { $clientId = $v }
            }
        }
    }

    if ($brokerHost) {
        Add-Result 'broker_host set' 'pass' "$brokerHost"
    } else {
        Add-Result 'broker_host set' 'warn' 'not specified - defaults to appserv1.local' `
            "Add  broker_host = your.broker  inside KSPBRIDGE { } in Settings.cfg."
    }

    if ($brokerPort -and ($brokerPort -as [int])) {
        Add-Result 'broker_port set' 'pass' "$brokerPort"
    } else {
        Add-Result 'broker_port set' 'warn' "missing or non-numeric ($brokerPort) - defaults to 1883" `
            "Add  broker_port = 1883  inside KSPBRIDGE { } in Settings.cfg."
    }

    if ($topicPrefix) {
        Add-Result 'topic_prefix set' 'pass' "$topicPrefix"
    } else {
        Add-Result 'topic_prefix set' 'warn' 'not specified - defaults to ksp/telemetry' ''
    }

    if ($clientId) {
        Add-Result 'client_id set' 'pass' "$clientId"
    } else {
        Add-Result 'client_id set' 'warn' 'not specified - defaults to kspbridge' `
            "Required by some brokers. Add  client_id = kspbridge  inside KSPBRIDGE { } in Settings.cfg."
    }
}

# Effective values used by the network checks below.
$effHost = if ($brokerHost) { $brokerHost } else { 'appserv1.local' }
$effPort = if ($brokerPort -and ($brokerPort -as [int])) { [int]$brokerPort } else { 1883 }
$effPrefix = if ($topicPrefix) { $topicPrefix } else { 'ksp/telemetry' }

# ---------------------------------------------------------------
# 5. TCP broker connectivity
# ---------------------------------------------------------------
Section ("5. Broker TCP connectivity (" + $effHost + ":" + $effPort + ")")

$tcpOk = $false
try {
    $tcp = Test-NetConnection -ComputerName $effHost -Port $effPort -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($tcp) {
        Add-Result ("TCP connect to " + $effHost + ":" + $effPort) 'pass' 'open'
        $tcpOk = $true
    } else {
        Add-Result ("TCP connect to " + $effHost + ":" + $effPort) 'fail' 'closed or unreachable' `
            "Confirm the MQTT broker is running on that host/port.
On Windows mosquitto runs as a service - check  Get-Service mosquitto .
If mosquitto runs locally, set  broker_host = localhost  in Settings.cfg."
    }
} catch {
    Add-Result ("TCP connect to " + $effHost + ":" + $effPort) 'fail' $_.Exception.Message `
        'Network or DNS issue - verify the host resolves and is reachable.'
}

# ---------------------------------------------------------------
# 6. MQTT round-trip via mosquitto_pub + mosquitto_sub
# ---------------------------------------------------------------
Section ("6. MQTT round-trip on " + $effPrefix + "/_install_check")

$mosquittoSub = 'C:\Program Files\mosquitto\mosquitto_sub.exe'
$mosquittoPub = 'C:\Program Files\mosquitto\mosquitto_pub.exe'

if (-not (Test-Path $mosquittoSub) -or -not (Test-Path $mosquittoPub)) {
    Add-Result 'mosquitto client tools present' 'warn' 'mosquitto_pub / mosquitto_sub not found' `
        "Install mosquitto from https://mosquitto.org/download/.
Without these, install-check skips the MQTT round-trip (the plugin's
own connection still works as long as TCP check above passed)."
} elseif (-not $tcpOk) {
    Add-Result 'MQTT round-trip' 'warn' 'skipped - TCP check failed above' ''
} else {
    # Process-unique topic + payload so parallel install-checks
    # don't collide. The sub-topic /_install_check/ is invisible
    # to KSPBridge consumers, who subscribe to specific topics.
    $checkTopic = "$effPrefix/_install_check/$([System.Guid]::NewGuid().ToString('N').Substring(0,8))"
    $payload = "install-check-$([DateTime]::UtcNow.ToString('o'))"

    # mosquitto_sub with -C 1 exits after one received message,
    # which makes it a perfect round-trip target. -W bounds the
    # wait at 5 seconds so a misconfigured broker doesn't hang us.
    $tmpOut = [IO.Path]::GetTempFileName()
    $tmpErr = [IO.Path]::GetTempFileName()
    $subProc = Start-Process -FilePath $mosquittoSub `
        -ArgumentList '-h', $effHost, '-p', $effPort, '-t', $checkTopic, '-C', '1', '-W', '5' `
        -RedirectStandardOutput $tmpOut -RedirectStandardError $tmpErr `
        -PassThru -NoNewWindow

    Start-Sleep -Milliseconds 500   # let the subscriber bind

    # Use System.Diagnostics.Process for the publish so this script
    # is safe to invoke from automation contexts where PowerShell's
    # call operator can lose the child's exit signal mid-pipeline.
    $pubInfo = New-Object System.Diagnostics.ProcessStartInfo
    $pubInfo.FileName = $mosquittoPub
    $pubInfo.Arguments = "-h $effHost -p $effPort -t `"$checkTopic`" -m `"$payload`""
    $pubInfo.UseShellExecute = $false
    $pubInfo.RedirectStandardOutput = $true
    $pubInfo.RedirectStandardError = $true
    $pubProc = [System.Diagnostics.Process]::Start($pubInfo)
    $pubProc.WaitForExit()

    if (-not $subProc.WaitForExit(5000)) {
        try { $subProc.Kill() } catch {}
        Add-Result 'MQTT round-trip' 'fail' 'subscriber timed out - message did not arrive' `
            "Broker accepted the TCP connect but did not relay the message.
Check broker logs:
  Get-Content C:\ProgramData\mosquitto\mosquitto.log -Tail 20"
    } else {
        $received = (Get-Content $tmpOut -ErrorAction SilentlyContinue) -join "`n"
        if ($received.Trim() -eq $payload) {
            Add-Result 'MQTT round-trip' 'pass' "publish + subscribe + receive succeeded"
        } else {
            Add-Result 'MQTT round-trip' 'fail' "received '$($received.Trim())', expected '$payload'" `
                "Subscriber returned a different payload - message ordering / persistence issue?"
        }
    }
    Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
}

# ---------------------------------------------------------------
# 7. WebSocket listener
# ---------------------------------------------------------------
Section ("7. WebSocket listener (" + $effHost + ":" + $WebSocketPort + ")")

try {
    $ws = Test-NetConnection -ComputerName $effHost -Port $WebSocketPort -InformationLevel Quiet -WarningAction SilentlyContinue
    if ($ws) {
        Add-Result ("TCP connect to " + $effHost + ":" + $WebSocketPort) 'pass' 'open (FDO browser console reachable)'
    } else {
        Add-Result ("TCP connect to " + $effHost + ":" + $WebSocketPort) 'warn' 'closed' `
            "The MQTT plugin does not need this - only the FDO browser console does.
To enable on a Windows mosquitto service, edit (as Administrator):
  C:\Program Files\mosquitto\mosquitto.conf
adding:
  listener $WebSocketPort
  protocol websockets
  allow_anonymous true
then restart the service:
  net stop mosquitto && net start mosquitto"
    }
} catch {
    Add-Result ("TCP connect to " + $effHost + ":" + $WebSocketPort) 'warn' $_.Exception.Message ''
}

# ---------------------------------------------------------------
# 8. FDO console assets
# ---------------------------------------------------------------
Section '8. FDO browser console assets'

# install-check ships under GameData/KSPBridge/, so the consoles
# directory in a source checkout lives several levels up. Released
# zips do not bundle the consoles - the FDO console is a source-
# only deliverable. Probe several plausible layouts.
$consoleCandidates = @(
    (Join-Path $PSScriptRoot '..\..\..\..\consoles\hard-scifi'),    # in repo (scripts/)
    (Join-Path $PSScriptRoot '..\consoles\hard-scifi'),             # if zip ever bundles
    'C:\Users\john_\dev\KSPBridge\consoles\hard-scifi'              # last-ditch
)
$consoleDir = $consoleCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($consoleDir) {
    $consoleDir = (Resolve-Path $consoleDir).Path
    foreach ($html in 'hardscifi-fdo-console.html', 'hardscifi-fdo-console-cdn.html') {
        $p = Join-Path $consoleDir $html
        if (Test-Path $p) {
            Add-Result $html 'pass' "$([int]((Get-Item $p).Length)) bytes at $consoleDir"
        } else {
            Add-Result $html 'warn' 'missing' ''
        }
    }
} else {
    Add-Result 'FDO console source' 'warn' 'consoles/hard-scifi/ not found' `
        "The browser console lives in the source repo at consoles/hard-scifi/.
Released zips do not include it - clone https://github.com/johnmknight/KSPBridge
and serve from there if you want the FDO console."
}

# ---------------------------------------------------------------
# 9. Python availability
# ---------------------------------------------------------------
Section '9. Python (for the console web server)'

$python = (Get-Command python -ErrorAction SilentlyContinue).Source
if (-not $python) {
    $python = (Get-Command py -ErrorAction SilentlyContinue).Source
}
if ($python) {
    $pyVer = & $python --version 2>&1
    Add-Result 'python on PATH' 'pass' "$python ($pyVer)"
} else {
    Add-Result 'python on PATH' 'warn' 'not found' `
        "Install Python 3 from https://python.org or the Microsoft Store.
Python is only used to serve the FDO browser console
(python -m http.server). The plugin itself does not need it."
}

# ---------------------------------------------------------------
# 10. Web server smoke test
# ---------------------------------------------------------------
Section '10. Web server smoke test'

if (-not $python) {
    Add-Result 'Web server smoke test' 'warn' 'skipped - no python available' ''
} elseif (-not $consoleDir) {
    Add-Result 'Web server smoke test' 'warn' 'skipped - console dir not found' ''
} else {
    # Pick a free ephemeral port to avoid collisions with anything
    # the user already has bound on 8000.
    $listener = New-Object System.Net.Sockets.TcpListener([System.Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $freePort = $listener.LocalEndpoint.Port
    $listener.Stop()

    $serverProc = Start-Process -FilePath $python `
        -ArgumentList '-m', 'http.server', $freePort `
        -WorkingDirectory $consoleDir -PassThru -WindowStyle Hidden

    try {
        $url = "http://localhost:$freePort/hardscifi-fdo-console.html"
        $started = $false
        $resp = $null
        for ($i = 0; $i -lt 15; $i++) {
            try {
                $resp = Invoke-WebRequest -Uri $url -UseBasicParsing -TimeoutSec 2
                if ($resp.StatusCode -eq 200) { $started = $true; break }
            } catch { Start-Sleep -Milliseconds 200 }
        }
        if ($started) {
            $sizeKB = [math]::Round($resp.Content.Length / 1024, 1)
            Add-Result 'http.server serves the FDO console' 'pass' `
                "fetched $url -> HTTP 200, $sizeKB KB"
        } else {
            Add-Result 'http.server serves the FDO console' 'fail' `
                "could not fetch $url within 3 seconds" `
                "Try running  python -m http.server  manually in $consoleDir
to see the actual error."
        }
    } finally {
        try { Stop-Process -Id $serverProc.Id -Force -ErrorAction SilentlyContinue } catch {}
    }
}

# ---------------------------------------------------------------
# Summary
# ---------------------------------------------------------------
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$passes = ($script:Results | Where-Object Status -eq 'pass').Count
$warns  = ($script:Results | Where-Object Status -eq 'warn').Count
$fails  = ($script:Results | Where-Object Status -eq 'fail').Count
Write-Host ("  Pass: $passes") -ForegroundColor Green
Write-Host ("  Warn: $warns") -ForegroundColor Yellow
Write-Host ("  Fail: $fails") -ForegroundColor Red
Write-Host ""

if ($fails -eq 0) {
    Write-Host "KSPBridge is ready. Launch KSP and watch your subscriber." -ForegroundColor Green
    if ($warns -gt 0) {
        Write-Host "Warnings above are non-blocking - the plugin will work but" -ForegroundColor Yellow
        Write-Host "the FDO browser console may not connect / KSP-AVC will not" -ForegroundColor Yellow
        Write-Host "notify of updates / etc. Address them at your convenience." -ForegroundColor Yellow
    }
    exit 0
} else {
    Write-Host "Address the failures above before launching KSP." -ForegroundColor Red
    exit 1
}
