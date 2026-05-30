@echo off
rem Smart launcher: open Klipy in browser, starting the server first if needed.
rem - If server already running on :8765, just opens the browser.
rem - Otherwise launches start_hidden.vbs, waits until server responds, then opens browser.
setlocal
set "ROOT=%~dp0"

powershell -NoProfile -Command ^
  "try { $r = Invoke-WebRequest -Uri http://127.0.0.1:8765/api/ping -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop; exit 0 } catch { exit 1 }"

if errorlevel 1 (
    wscript "%ROOT%start_hidden.vbs"
    powershell -NoProfile -Command ^
      "for ($i=0; $i -lt 60; $i++) { try { Invoke-WebRequest -Uri http://127.0.0.1:8765/api/ping -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop | Out-Null; exit 0 } catch { Start-Sleep -Milliseconds 200 } }; exit 1"
    if errorlevel 1 (
        echo Nie udalo sie wystartowac serwera. Sprawdz data\server.log
        pause
        exit /b 1
    )
)

start msedge --app="http://127.0.0.1:8765/"
endlocal
