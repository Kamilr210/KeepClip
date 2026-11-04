@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\quality-check.ps1" -Mode Full -OpenReport
set "qaExit=%errorlevel%"
if not "%qaExit%"=="0" (
    echo.
    echo Testy zakonczone bledem. Szczegoly znajdziesz w raporcie.
    pause
)
exit /b %qaExit%
