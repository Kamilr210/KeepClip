' Launches start_server.bat completely hidden (no window flash, no console).
' Window style 0 = hidden, second arg False = don't wait.
Set objShell = WScript.CreateObject("WScript.Shell")
Set fso = WScript.CreateObject("Scripting.FileSystemObject")
root = fso.GetParentFolderName(WScript.ScriptFullName)
objShell.Run """" & root & "\start_server.bat""", 0, False
