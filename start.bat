@echo off
setlocal
cd /d "%~dp0backend"
set "PATH=%~dp0tools\bin;%PATH%"
"%~dp0backend\.venv\Scripts\python.exe" -m uvicorn app:app --host 127.0.0.1 --port 8765
endlocal
