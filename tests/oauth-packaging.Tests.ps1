[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$prepare = Join-Path $repo 'installer\prepare-public-oauth.ps1'
$temporary = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixture = Join-Path $temporary ('keepclip-public-oauth-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$inputFile = Join-Path $fixture 'private-input.json'
$outputFile = Join-Path $fixture 'publication\data\google_client.json'
$utf8 = New-Object Text.UTF8Encoding($false)
$script:passed = 0

function Run-Prepare([string[]]$Arguments, [bool]$Success) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = (& powershell -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $prepare @Arguments 2>&1 | Out-String); $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if (($code -eq 0) -ne $Success) { throw "Unexpected preparation result: $code. $output" }
    if ($output.Contains('SYNTHETIC_PRIVATE_SECRET')) { throw 'Private configuration was printed in diagnostics.' }
    $script:passed++
}

try {
    $privateJson = '{"installed":{"client_id":"test-id.apps.googleusercontent.com","client_secret":"SYNTHETIC_PRIVATE_SECRET","project_id":"private-project","redirect_uris":["http://localhost"]},"private_key":"SYNTHETIC_PRIVATE_SECRET"}'
    [IO.File]::WriteAllText($inputFile, $privateJson, $utf8)
    $inputHash = (Get-FileHash -LiteralPath $inputFile).Hash
    Run-Prepare @('-InputPath',$inputFile,'-OutputPath',$outputFile) $true
    Run-Prepare @('-InputPath',$outputFile,'-VerifyOnly') $true
    $publicJson = [IO.File]::ReadAllText($outputFile)
    $public = $publicJson | ConvertFrom-Json
    if ($public.installed.client_id -ne 'test-id.apps.googleusercontent.com' -or @($public.PSObject.Properties.Name).Count -ne 1 -or @($public.installed.PSObject.Properties.Name).Count -ne 1) { throw 'Public configuration is not minimal.' }
    if ($publicJson.Contains('SYNTHETIC_PRIVATE_SECRET') -or $publicJson.Contains('client_secret') -or $publicJson.Contains('private_key')) { throw 'Secret included in public output.' }
    if ((Get-FileHash -LiteralPath $inputFile).Hash -ne $inputHash) { throw 'Original configuration was modified.' }
    $script:passed++

    Run-Prepare @('-InputPath',$inputFile,'-VerifyOnly') $false
    Run-Prepare @('-InputPath',$inputFile,'-OutputPath',$inputFile) $false
    foreach ($bad in @('{}','null','[]','{"web":{"client_id":"test-id","client_secret":"SYNTHETIC_PRIVATE_SECRET"}}','{"client_id":"test-id"}','{"installed":null}','{"installed":{"client_id":123}}','{"installed":{"client_id":""}}','{"installed":{"client_id":"bad id"}}','{"installed":{"client_id":"test-id"},"web":{}}','{"installed":')) {
        [IO.File]::WriteAllText($inputFile, $bad, $utf8)
        $savedHash = (Get-FileHash -LiteralPath $outputFile).Hash
        Run-Prepare @('-InputPath',$inputFile,'-OutputPath',$outputFile) $false
        if ((Get-FileHash -LiteralPath $outputFile).Hash -ne $savedHash) { throw 'Failed preparation changed existing output.' }
    }
    foreach ($extra in @('{"installed":{"client_id":"test-id","client_secret":null}}','{"installed":{"client_id":"test-id"},"extra":"value"}','{"Installed":{"client_id":"test-id"}}','{"installed":{"Client_Id":"test-id"}}','{"installed":{"client_id":"bad\u007fid"}}')) {
        [IO.File]::WriteAllText($inputFile, $extra, $utf8)
        Run-Prepare @('-InputPath',$inputFile,'-VerifyOnly') $false
    }
    [IO.File]::WriteAllText($inputFile, '{"installed":{"client_id":"test-id"}}', $utf8)
    Run-Prepare @('-InputPath',$inputFile,'-OutputPath',$outputFile) $true
    Run-Prepare @('-InputPath',$outputFile,'-VerifyOnly') $true
    Write-Host "PASS: $script:passed OAuth packaging checks; secrets excluded and input preserved."
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ($resolved.StartsWith($temporary.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
