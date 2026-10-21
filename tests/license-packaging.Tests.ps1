[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$PublishDir)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Check = Join-Path $RepoRoot 'installer\verify-licenses.ps1'
$Stage = Join-Path $RepoRoot 'installer\stage-licenses.ps1'
$Manifest = Get-Content -LiteralPath (Join-Path $RepoRoot 'licenses\manifest.json') -Raw | ConvertFrom-Json
$TempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$Fixture = Join-Path $TempRoot ('keepclip-license-tests-'+[Guid]::NewGuid().ToString('N'))
$script:Passed = 0
function Invoke-Check([string[]]$Arguments, [string]$ExpectedError) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = (& powershell -NoProfile -ExecutionPolicy Bypass -File $Check -PublishDir $Fixture @Arguments 2>&1 | Out-String); $code=$LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if ($ExpectedError) {
        if ($code -eq 0 -or $output -notmatch $ExpectedError) { throw "Oczekiwano '$ExpectedError', kod $code. $output" }
    } elseif ($code -ne 0) { throw "Kontrola poprawnej publikacji nie przeszla. $output" }
    $script:Passed++
}
function With-ModifiedFile($relative, $content, [scriptblock]$Assertion) {
    $file=Join-Path $Fixture $relative
    $saved=[IO.File]::ReadAllBytes($file)
    try { [IO.File]::WriteAllText($file,$content,(New-Object Text.UTF8Encoding($false))); & $Assertion }
    finally { [IO.File]::WriteAllBytes($file,$saved) }
}
try {
    & $Check -PublishDir $PublishDir
    New-Item -ItemType Directory -Force -Path $Fixture | Out-Null
    Copy-Item -LiteralPath (Join-Path $PublishDir 'licenses') -Destination $Fixture -Recurse
    Copy-Item -LiteralPath (Join-Path $PublishDir 'THIRD-PARTY-NOTICES.md'),(Join-Path $PublishDir 'KeepClip.deps.json') -Destination $Fixture
    foreach ($native in $Manifest.nativeAssets) {
        $dst=Join-Path $Fixture $native.path
        New-Item -ItemType Directory -Force -Path (Split-Path $dst) | Out-Null
        Copy-Item -LiteralPath (Join-Path $PublishDir $native.path) -Destination $dst
    }
    $sourceBuild=Get-Content -LiteralPath (Join-Path $Fixture 'licenses\ffmpeg-build\provenance.json') -Raw | ConvertFrom-Json
    if($sourceBuild.schemaVersion -eq 2) {
        $fixtureBin=Join-Path $Fixture 'tools\bin'
        New-Item -ItemType Directory -Force -Path $fixtureBin|Out-Null
        foreach($name in @('ffmpeg.exe','ffprobe.exe')) {Copy-Item -LiteralPath (Join-Path $PublishDir ('tools\bin\'+$name)) -Destination $fixtureBin}
        Invoke-Check @('-RequireFfmpeg','-ForDistribution') $null
        With-ModifiedFile 'licenses\ffmpeg-build\sources\ffmpeg-9.0.2.tar.xz' 'changed corresponding source' {Invoke-Check @('-ForDistribution') 'Brak lub zmiana FFmpeg'}
        With-ModifiedFile 'licenses\ffmpeg-build\build\build.sh' 'changed compiler recipe' {Invoke-Check @('-ForDistribution') 'Brak lub zmiana FFmpeg'}
        With-ModifiedFile 'tools\bin\ffmpeg.exe' 'changed FFmpeg executable' {Invoke-Check @('-ForDistribution') 'rozni sie od zweryfikowanego oryginalu'}
        $sourceFile=Join-Path $Fixture 'licenses\ffmpeg-build\sources\x264.tar.gz'
        $saved=[IO.File]::ReadAllBytes($sourceFile)
        try {Remove-Item -LiteralPath $sourceFile; Invoke-Check @('-ForDistribution') 'Brak lub zmiana FFmpeg'}
        finally {[IO.File]::WriteAllBytes($sourceFile,$saved)}
    }
    @{schemaVersion=1;binaries=@();correspondingSourceStatus='unverified';distributionAllowed=$false} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $Fixture 'licenses\ffmpeg-build\provenance.json') -Encoding UTF8
    Invoke-Check @() $null
    $notice=Join-Path $Fixture 'licenses\third-party\webview2\1.0.3967.48\NOTICE.txt'
    $noticeBackup=[IO.File]::ReadAllBytes($notice)
    try { Remove-Item -LiteralPath $notice; Invoke-Check @() 'Brak wymaganego materialu' }
    finally { [IO.File]::WriteAllBytes($notice,$noticeBackup) }
    With-ModifiedFile 'licenses\third-party\SQLitePCLRaw\2.1.12\NOTICE.TXT' 'altered upstream attribution' {Invoke-Check @() 'rozni sie od zweryfikowanego oryginalu'}
    $deps=Get-Content -LiteralPath (Join-Path $Fixture 'KeepClip.deps.json') -Raw | ConvertFrom-Json
    $deps.libraries | Add-Member NoteProperty 'Unreviewed.Dependency/1.0.0' @{type='package'}
    With-ModifiedFile 'KeepClip.deps.json' ($deps|ConvertTo-Json -Depth 100) {Invoke-Check @() 'Unreviewed.Dependency/1.0.0'}
    $deps=Get-Content -LiteralPath (Join-Path $Fixture 'KeepClip.deps.json') -Raw | ConvertFrom-Json
    $runtimeKey=@($deps.libraries.PSObject.Properties | Where-Object {$_.Name -like 'runtimepack.Microsoft.NETCore*'})[0].Name
    $runtimeEntry=$deps.libraries.$runtimeKey
    $deps.libraries.PSObject.Properties.Remove($runtimeKey)
    $deps.libraries | Add-Member NoteProperty 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/99.0.0' $runtimeEntry
    With-ModifiedFile 'KeepClip.deps.json' ($deps|ConvertTo-Json -Depth 100) {Invoke-Check @() '99.0.0'}
    With-ModifiedFile $Manifest.nativeAssets[0].path 'changed native binary' {Invoke-Check @() 'rozni sie od zweryfikowanego oryginalu'}
    Invoke-Check @('-RequireFfmpeg') 'Brak ffmpeg.exe'
    Invoke-Check @('-ForDistribution') 'DYSTRYBUCJA ZABLOKOWANA'
    $fakeAssets=@{libraries=@{}}
    foreach($package in $Manifest.packages){$fakeAssets.libraries[$package.id+'/'+$package.version]=@{type='package';sha512=$package.nugetContentHash}}
    $assetsPath=Join-Path $Fixture 'assets.json'
    $fakeAssets|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $assetsPath -Encoding UTF8
    Invoke-Check @('-AssetsFile',$assetsPath) $null
    $fakeAssets.libraries['Whisper.net/1.9.1'].sha512='different package content'
    $fakeAssets|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $assetsPath -Encoding UTF8
    Invoke-Check @('-AssetsFile',$assetsPath) 'Zmienila sie zawartosc pakietu'
    $fakeAssets.libraries['Whisper.net/1.9.1'].sha512=@($Manifest.packages|Where-Object {$_.id -eq 'Whisper.net'})[0].nugetContentHash
    $fakeAssets.libraries['Unreviewed.Dependency/1.0.0']=@{type='package';sha512='unreviewed'}
    $fakeAssets|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $assetsPath -Encoding UTF8
    Invoke-Check @('-AssetsFile',$assetsPath) 'Zmiana zaleznosci NuGet'
    $fakeBin=Join-Path $Fixture 'tools\bin'
    New-Item -ItemType Directory -Force -Path $fakeBin|Out-Null
    [IO.File]::WriteAllText((Join-Path $fakeBin 'ffmpeg.exe'),'unrecognized binary')
    $previous=$ErrorActionPreference
    $ErrorActionPreference='Continue'
    try {$stageOutput=(& powershell -NoProfile -ExecutionPolicy Bypass -File $Stage -PublishDir $Fixture 2>&1|Out-String); $stageCode=$LASTEXITCODE}
    finally {$ErrorActionPreference=$previous}
    if($stageCode -eq 0 -or $stageOutput -notmatch 'Nieznany build ffmpeg.exe'){throw "Nie rozpoznano obcego FFmpeg. $stageOutput"}
    $script:Passed++
    Write-Host "PASS: $script:Passed kontrole pakowania (w tym brak/zmiana NOTICE, nowe wersje, natywne DLL, obcy FFmpeg i blokada dystrybucji)."
} finally {
    $resolved=[IO.Path]::GetFullPath($Fixture)
    if($resolved.StartsWith($TempRoot.TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $resolved)) {Remove-Item -LiteralPath $resolved -Recurse -Force}
}
