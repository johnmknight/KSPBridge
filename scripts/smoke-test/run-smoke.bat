@echo off
rem ============================================================
rem  KSPBridge single-machine smoke test
rem ============================================================
rem  What this does:
rem    1. Backs up the deployed Settings.cfg and writes a
rem       smoke-test override pointing at localhost:1885.
rem    2. Starts a user-mode mosquitto broker (1885 + 9003 WS)
rem       in its own console window.
rem    3. Starts mosquitto_sub against localhost:1885 in its
rem       own window so you can watch all 18 topics tick live.
rem    4. Starts a Python HTTP server in consoles\hard-scifi
rem       so the FDO console can be loaded in a browser.
rem    5. Opens the browser to the console with the
rem       ?broker=ws://localhost:9003 override applied.
rem    6. Prints instructions to launch KSP from Steam.
rem
rem  When you're done testing, run cleanup-smoke.bat in the
rem  same directory to restore the original Settings.cfg.
rem ============================================================

setlocal

set REPO=%~dp0..\..
set KSP_ROOT=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
set KSP_CFG=%KSP_ROOT%\GameData\KSPBridge\Settings.cfg
set MOSQUITTO=C:\Program Files\mosquitto\mosquitto.exe
set MOSQUITTO_SUB=C:\Program Files\mosquitto\mosquitto_sub.exe
set CONSOLE_DIR=%REPO%\consoles\hard-scifi
set CONSOLE_URL=http://localhost:8000/hardscifi-fdo-console.html?broker=ws://localhost:9003

echo === KSPBridge smoke test ===
echo.

rem ---- Sanity-check prerequisites ------------------------------
if not exist "%MOSQUITTO%" (
    echo ERROR: mosquitto.exe not found at "%MOSQUITTO%"
    echo Install mosquitto from https://mosquitto.org/download/
    goto :fail
)
if not exist "%MOSQUITTO_SUB%" (
    echo ERROR: mosquitto_sub.exe not found at "%MOSQUITTO_SUB%"
    goto :fail
)
if not exist "%KSP_CFG%" (
    echo ERROR: KSPBridge is not deployed. Expected:
    echo   %KSP_CFG%
    echo Build the plugin first ^(scripts\make-release.ps1 deploys to KSP^).
    goto :fail
)

rem ---- Back up + override Settings.cfg -------------------------
if not exist "%KSP_CFG%.smoke-backup" (
    echo Backing up Settings.cfg to Settings.cfg.smoke-backup
    copy /Y "%KSP_CFG%" "%KSP_CFG%.smoke-backup" >nul
) else (
    echo Settings.cfg backup already exists, leaving as-is.
)

echo Writing smoke-test Settings.cfg ^(localhost:1885^)
> "%KSP_CFG%" echo // Smoke-test override - restore via cleanup-smoke.bat
>> "%KSP_CFG%" echo KSPBRIDGE
>> "%KSP_CFG%" echo {
>> "%KSP_CFG%" echo     broker_host = localhost
>> "%KSP_CFG%" echo     broker_port = 1885
>> "%KSP_CFG%" echo     topic_prefix = ksp/telemetry
>> "%KSP_CFG%" echo     client_id = kspbridge
>> "%KSP_CFG%" echo }

rem ---- Launch broker / subscriber / web server ----------------
echo Starting mosquitto broker in a new window ^(1885 TCP, 9003 WS^)
start "KSPBridge smoke - broker"            "%MOSQUITTO%" -c "%~dp0mosquitto.conf" -v

echo Starting mosquitto_sub in a new window ^(localhost:1885^)
start "KSPBridge smoke - subscriber"        "%MOSQUITTO_SUB%" -h localhost -p 1885 -t "ksp/telemetry/#" -v

echo Starting Python HTTP server in consoles\hard-scifi
start "KSPBridge smoke - HTTP server"       cmd /k "cd /d %CONSOLE_DIR% && python -m http.server 8000"

rem Give the HTTP server a brief moment to bind before opening the browser.
timeout /t 2 /nobreak >nul

echo Opening FDO console in default browser
start "" "%CONSOLE_URL%"

echo.
echo ============================================================
echo  All smoke-test infrastructure is up.
echo.
echo  Next:
echo    Launch Kerbal Space Program from Steam ^(or run^):
echo      start steam://rungameid/220200
echo.
echo    Within a few seconds of the main menu, the subscriber
echo    window should print:
echo      ksp/telemetry/_bridge/status {"online":true,"version":"0.15.0",...}
echo.
echo    Load any vessel into flight; all 18 topics should start
echo    ticking and the FDO console webpage should populate.
echo.
echo  When done:
echo    Run cleanup-smoke.bat to restore the original
echo    Settings.cfg ^(your homelab broker target^).
echo    Close the broker / subscriber / HTTP server windows
echo    manually, or just close the consoles.
echo ============================================================
echo.
exit /b 0

:fail
echo.
echo Smoke test setup aborted.
exit /b 1
