' Uruchamia KeepClip w tle (bez migajacego okna konsoli) i otwiera przegladarke.
' Skrot z instalatora wskazuje na ten plik.
Set sh = WScript.CreateObject("WScript.Shell")
Set fso = WScript.CreateObject("Scripting.FileSystemObject")
root = fso.GetParentFolderName(WScript.ScriptFullName)
' Window style 0 = ukryte, False = nie czekaj.
sh.Run """" & root & "\klipy.cmd""", 0, False
