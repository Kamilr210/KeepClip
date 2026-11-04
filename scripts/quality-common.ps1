Set-StrictMode -Version Latest

function Invoke-QualityCommand([string]$FilePath, [string[]]$Arguments, [string]$LogPath) {
    $null = Get-Command $FilePath -CommandType Application -ErrorAction Stop
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        $global:LASTEXITCODE = -1
        & $FilePath @Arguments 2>&1 | ForEach-Object { $_.ToString() } | Tee-Object -FilePath $LogPath -Append | ForEach-Object { Write-Host $_ }
        $code = $global:LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if ($code -ne 0) { throw "$FilePath failed with exit code $code. See $LogPath" }
}

function Find-QualityCoverage([string]$Directory) {
    $reports = @(Get-ChildItem -LiteralPath $Directory -Filter 'coverage.cobertura.xml' -Recurse -File)
    if ($reports.Count -eq 0) { throw 'No fresh coverage report was produced.' }
    $hashes = @($reports | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } | Sort-Object -Unique)
    if ($hashes.Count -ne 1) { throw "Found $($hashes.Count) different coverage reports; refusing to report incomplete or double-counted coverage." }
    return ($reports | Sort-Object { $_.FullName.Length } | Select-Object -First 1).FullName
}

function Read-QualityTestResult([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Missing test results: $Path" }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $counters = $trx.SelectSingleNode('/*[local-name()="TestRun"]/*[local-name()="ResultSummary"]/*[local-name()="Counters"]')
    if ($null -eq $counters) { throw "Missing test counters: $Path" }
    $total = [int]$counters.GetAttribute('total')
    $executed = [int]$counters.GetAttribute('executed')
    $passed = [int]$counters.GetAttribute('passed')
    $outcome = $counters.ParentNode.GetAttribute('outcome')
    if ($outcome -ne 'Completed' -or $total -lt 1 -or $executed -ne $total -or $passed -ne $total) { throw "Incomplete or failed test run: outcome=$outcome, total=$total, executed=$executed, passed=$passed. See $Path" }
    return [pscustomobject]@{ total = $total; executed = $executed; passed = $passed; path = $Path }
}

function Write-QualityCoverage([string]$CoberturaPath, [string]$OutputDirectory) {
    if (-not (Test-Path -LiteralPath $CoberturaPath -PathType Leaf)) { throw "Missing coverage: $CoberturaPath" }
    [xml]$document = Get-Content -LiteralPath $CoberturaPath -Raw
    $coverage = $document.SelectSingleNode('/coverage')
    if ($null -eq $coverage -or [int]$coverage.GetAttribute('lines-valid') -lt 1) { throw 'Coverage contains no instrumented lines.' }
    $packages = @($document.SelectNodes('/coverage/packages/package'))
    if ($packages.Count -ne 1 -or $packages[0].GetAttribute('name') -ne 'KeepClip') { throw 'Coverage must contain the KeepClip assembly only.' }
    $files = @{}
    foreach ($type in $document.SelectNodes('/coverage/packages/package/classes/class')) {
        $filename = $type.GetAttribute('filename').Replace('\', '/')
        if (-not $files.ContainsKey($filename)) { $files[$filename] = @{} }
        foreach ($line in $type.SelectNodes('lines/line')) {
            $number = [int]$line.GetAttribute('number')
            $hits = [int]$line.GetAttribute('hits')
            if (-not $files[$filename].ContainsKey($number) -or $hits -gt $files[$filename][$number]) { $files[$filename][$number] = $hits }
        }
    }
    $rows = @(foreach ($filename in $files.Keys) {
        $valid = $files[$filename].Count
        $covered = @($files[$filename].Values | Where-Object { $_ -gt 0 }).Count
        [pscustomobject]@{ file = $filename; covered = $covered; total = $valid; percent = $(if ($valid) { [math]::Round(100 * $covered / $valid, 2) } else { 0 }) }
    }) | Sort-Object percent, file
    $valid = [int]$coverage.GetAttribute('lines-valid')
    $covered = [int]$coverage.GetAttribute('lines-covered')
    $result = [pscustomobject]@{
        assembly = 'KeepClip'; coveredLines = $covered; totalLines = $valid
        linePercent = [math]::Round(100 * $covered / $valid, 2)
        coveredBranches = [int]$coverage.GetAttribute('branches-covered'); totalBranches = [int]$coverage.GetAttribute('branches-valid')
        files = @($rows)
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'coverage-summary.json') -Encoding UTF8
    $markdown = @('# KeepClip coverage', '', "Lines: $covered / $valid ($($result.linePercent)%). No coverage threshold is enforced.", '', 'All instrumented KeepClip code is included, including GUI and hardware adapters. Tests are excluded.', '', '| File | Covered lines | Total lines | Coverage |', '| --- | ---: | ---: | ---: |')
    $htmlRows = foreach ($row in $rows) {
        $markdown += "| $($row.file.Replace('|', '\|')) | $($row.covered) | $($row.total) | $($row.percent)% |"
        '<tr><td>' + [Net.WebUtility]::HtmlEncode($row.file) + '</td><td>' + $row.covered + '</td><td>' + $row.total + '</td><td>' + $row.percent + '%</td></tr>'
    }
    $markdown | Set-Content -LiteralPath (Join-Path $OutputDirectory 'coverage-summary.md') -Encoding UTF8
    $html = '<!doctype html><html lang="en"><meta charset="utf-8"><title>KeepClip coverage</title><style>body{font:16px system-ui;margin:2rem;max-width:1200px}table{border-collapse:collapse;width:100%}td,th{padding:.5rem;border-bottom:1px solid #ddd;text-align:right}td:first-child,th:first-child{text-align:left}th{background:#eee}</style><h1>KeepClip coverage</h1><p>Lines: ' + $covered + ' / ' + $valid + ' (' + $result.linePercent + '%). No coverage threshold is enforced.</p><p>All instrumented KeepClip code is included. GUI and hardware adapters remain in the denominator; tests are excluded. Files are ordered by coverage to expose gaps.</p><table><thead><tr><th>File</th><th>Covered lines</th><th>Total lines</th><th>Coverage</th></tr></thead><tbody>' + ($htmlRows -join '') + '</tbody></table></html>'
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'coverage.html'), $html, (New-Object Text.UTF8Encoding($false)))
    return $result
}
