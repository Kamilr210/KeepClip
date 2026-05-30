@echo off
rem Silent server launcher - meant to be invoked via start_hidden.vbs.
rem Uses pythonw.exe so there's no console window, redirects everything to data/server.log.
setlocal
set "ROOT=%~dp0"
if not exist "%ROOT%data" mkdir "%ROOT%data"
set "PATH=%ROOT%tools\bin;%PATH%"
cd /d "%ROOT%backend"
"%ROOT%backend\.venv\Scripts\pythonw.exe" -m uvicorn app:app --host 127.0.0.1 --port 8765 --log-level info >> "%ROOT%data\server.log" 2>&1
endlocal
