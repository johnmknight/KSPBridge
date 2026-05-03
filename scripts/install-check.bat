@echo off
rem ============================================================
rem  KSPBridge install verification
rem ============================================================
rem  Wrapper around install-check.ps1 so users can double-click
rem  to run, without dealing with PowerShell's ExecutionPolicy
rem  prompt or having to know that pwsh is the right invocation.
rem
rem  Reports pass/warn/fail for every prerequisite and exits 0
rem  when everything required is in place.
rem ============================================================

setlocal

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-check.ps1" %*
set EXITCODE=%ERRORLEVEL%

echo.
pause
exit /b %EXITCODE%
