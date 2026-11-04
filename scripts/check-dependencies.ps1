[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$AssetsFile)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifest = Get-Content -LiteralPath (Join-Path $repo 'licenses\manifest.json') -Raw | ConvertFrom-Json
$assets = Get-Content -LiteralPath $AssetsFile -Raw | ConvertFrom-Json
$expected = @($manifest.packages | ForEach-Object { $_.id + '/' + $_.version } | Sort-Object)
$actual = @($assets.libraries.PSObject.Properties | Where-Object { $_.Value.type -eq 'package' } | ForEach-Object { $_.Name } | Sort-Object)
if (@(Compare-Object $expected $actual).Count) { throw 'Application dependencies differ from licenses/manifest.json. Review versions, hashes and license materials.' }
foreach ($package in $manifest.packages) {
    if ($assets.libraries.($package.id + '/' + $package.version).sha512 -ne $package.nugetContentHash) { throw "Package content changed: $($package.id)." }
}
foreach ($material in $manifest.materials) {
    $path = Join-Path $repo $material.path
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $material.sha256) { throw "Missing or modified license material: $($material.path)." }
}
[xml]$project = Get-Content -LiteralPath (Join-Path $repo 'KeepClip\KeepClip.csproj') -Raw
$runtime = $project.SelectSingleNode('//RuntimeFrameworkVersion').InnerText
foreach ($pack in $manifest.runtimePacks) {
    if ($pack.version -ne $runtime) { throw "Pinned runtime $runtime differs from license manifest runtime $($pack.id)/$($pack.version)." }
}
Write-Host "PASS: $($expected.Count) application dependencies, package hashes, license materials and pinned runtime. Installer staging remains verified by release.yml."
