@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\open-quality-report.ps1"
set "qaExit=%errorlevel%"
if not "%qaExit%"=="0" pause
exit /b %qaExit%
