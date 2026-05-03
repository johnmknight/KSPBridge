@echo off
rem ============================================================
rem  KSPBridge smoke-test cleanup
rem ============================================================
rem  Restores the deployed Settings.cfg from the backup made by
rem  run-smoke.bat, so the next KSP launch reverts to your
rem  configured broker host (homelab, etc.) rather than the
rem  smoke-test localhost:1885 override.
rem
rem  This script does NOT close the broker / subscriber / HTTP
rem  server windows opened by run-smoke.bat - close those by
rem  hand. They run in user mode and don't persist across
rem  reboots, so leaving them running until you close them is
rem  fine.
rem ============================================================

setlocal

set KSP_ROOT=C:\Program Files (x86)\Steam\steamapps\common\Kerbal Space Program
set KSP_CFG=%KSP_ROOT%\GameData\KSPBridge\Settings.cfg

echo === KSPBridge smoke-test cleanup ===
echo.

if exist "%KSP_CFG%.smoke-backup" (
    echo Restoring Settings.cfg from smoke-backup
    copy /Y "%KSP_CFG%.smoke-backup" "%KSP_CFG%" >nul
    del "%KSP_CFG%.smoke-backup"
    echo Settings.cfg restored.
) else (
    echo No smoke-backup found at "%KSP_CFG%.smoke-backup".
    echo Settings.cfg left as-is.
)

echo.
echo Smoke-test windows ^(broker / subscriber / HTTP server^) need
echo to be closed manually if you want to free their ports.
echo.
exit /b 0
