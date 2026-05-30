@echo off
rem Creates a desktop shortcut "Klipy" that runs klipy.cmd (silent — no console flash).
setlocal
set "ROOT=%~dp0"
set "TARGET=%ROOT%klipy.cmd"
set "DESKTOP=%USERPROFILE%\Desktop"
set "LNK=%DESKTOP%\Klipy.lnk"

powershell -NoProfile -ExecutionPolicy Bypass -Command "$s = New-Object -ComObject WScript.Shell; $lnk = $s.CreateShortcut('%LNK%'); $lnk.TargetPath = '%TARGET%'; $lnk.WorkingDirectory = '%ROOT%'; $lnk.WindowStyle = 7; $lnk.IconLocation = 'shell32.dll,137'; $lnk.Description = 'Klipy - wyszukiwarka po transkrypcji'; $lnk.Save()"

if exist "%LNK%" (
    echo OK: utworzono skrot na pulpicie: %LNK%
) else (
    echo BLAD: nie udalo sie utworzyc skrotu.
)
pause
endlocal
