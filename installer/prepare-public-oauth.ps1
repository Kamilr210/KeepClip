[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$InputPath, [string]$OutputPath, [switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
if ((Get-Item -LiteralPath $InputPath).Length -gt 65536) { throw 'Konfiguracja OAuth jest zbyt duza.' }
try { $client = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $InputPath)) | ConvertFrom-Json }
catch { throw 'Nieprawidlowy JSON konfiguracji OAuth.' }
if ($client -isnot [PSCustomObject] -or $client.PSObject.Properties.Name -contains 'web' -or $client.installed -isnot [PSCustomObject]) {
    throw 'Wymagany klient OAuth typu Desktop app (installed).'
}
$id = $client.installed.client_id
if ($id -isnot [string] -or [string]::IsNullOrWhiteSpace($id) -or $id.Length -gt 1024 -or $id -match '[\s\p{Cc}]') {
    throw 'Nieprawidlowy installed.client_id.'
}
if ($VerifyOnly) {
    if (@($client.PSObject.Properties.Name).Count -ne 1 -or $client.PSObject.Properties.Name -cnotcontains 'installed' -or @($client.installed.PSObject.Properties.Name).Count -ne 1 -or $client.installed.PSObject.Properties.Name -cnotcontains 'client_id') {
        throw 'Publiczna konfiguracja OAuth moze zawierac tylko installed.client_id.'
    }
    return
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) { throw 'Wymagany plik wyjsciowy publicznej konfiguracji.' }
$destination = [IO.Path]::GetFullPath($OutputPath)
if ($destination -eq (Resolve-Path -LiteralPath $InputPath).Path) { throw 'Plik wejsciowy nie moze byc nadpisany.' }
New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
$public = @{installed=@{client_id=$id}} | ConvertTo-Json -Depth 3
[IO.File]::WriteAllText($destination, $public, (New-Object Text.UTF8Encoding($false)))
