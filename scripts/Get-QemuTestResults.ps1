# KraicDev - validates and extracts data reports emitted by the isolated guest's virtual UART.
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath $config.serialLog)) { Write-Output 'Waiting for the guest serial report.'; exit 0 }
$stream = [IO.FileStream]::new($config.serialLog,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
try { if ($stream.Length -gt 64MB) { throw 'Guest serial output exceeds allowed bounds.' }; $reader=[IO.StreamReader]::new($stream); $serialText=$reader.ReadToEnd() } finally { $stream.Dispose() }
$lines = $serialText -split "`r?`n"
$status = @($lines | Where-Object { $_.StartsWith('SWAP_STATUS ') }) | Select-Object -Last 1
if ($status) { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($status.Substring(12))) | ConvertFrom-Json | Select-Object phase,detail | ConvertTo-Json }
$statusIndex=-1
for ($index=$lines.Length-1; $index -ge 0; $index--) { if ($lines[$index].StartsWith('SWAP_STATUS ')) { $statusIndex=$index; break } }
$endIndex=-1; $beginIndex=-1
for ($index=$lines.Length-1; $index -ge 0; $index--) {
    if ($endIndex -lt 0 -and $lines[$index] -match '^SWAP_END [A-F0-9]{64}$') { $endIndex=$index }
    elseif ($endIndex -ge 0 -and $lines[$index] -match '^SWAP_BEGIN [0-9]+$') { $beginIndex=$index; break }
}
if ($endIndex -lt 0) { Write-Output 'Guest report transfer is not complete yet.'; exit 0 }
if ($beginIndex -lt 0) { throw 'Guest report has no matching start marker.' }
if ($statusIndex -gt $beginIndex) { Write-Output 'The current guest run has not transferred its own report yet.'; exit 0 }
$end=$lines[$endIndex]; $declared=$lines[$beginIndex]
$expectedLength=[long]$declared.Substring(11)
if ($expectedLength -le 0 -or $expectedLength -gt 32MB) { throw 'Guest report size is outside allowed bounds.' }
$encoded = (@($lines[($beginIndex+1)..($endIndex-1)] | Where-Object { $_.StartsWith('SWAP_DATA ') } | ForEach-Object { $_.Substring(10) })) -join ''
if ($encoded.Length -ne ([Math]::Ceiling($expectedLength/3.0)*4)) { throw 'Guest report encoding length mismatch.' }
$bytes=[Convert]::FromBase64String($encoded)
if ($bytes.Length -ne $expectedLength) { throw 'Incomplete guest report.' }
$sha=[Security.Cryptography.SHA256]::Create()
try { $hash=([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','') } finally { $sha.Dispose() }
if ($hash -ne $end.Substring(9)) { throw 'Guest report checksum mismatch.' }
$output=Join-Path $root ('artifacts/vm/qemu-results/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
[void][IO.Directory]::CreateDirectory($output)
$archivePath=Join-Path $output 'guest-results.zip'
[IO.File]::WriteAllBytes($archivePath,$bytes)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive=[IO.Compression.ZipFile]::OpenRead($archivePath)
try {
    if ($archive.Entries.Count -gt 100 -or ($archive.Entries | Measure-Object Length -Sum).Sum -gt 256MB) { throw 'Guest report archive exceeds allowed bounds.' }
    $outputRoot=[IO.Path]::GetFullPath($output).TrimEnd('\')+'\'
    foreach ($entry in $archive.Entries) {
        $target=[IO.Path]::GetFullPath((Join-Path $output $entry.FullName))
        if ([IO.Path]::IsPathRooted($entry.FullName) -or !$target.StartsWith($outputRoot,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe guest archive path.' }
        if ($entry.Name.Length -gt 0 -and [IO.Path]::GetExtension($entry.Name) -notin '.json','.log','.png') { throw 'Only data reports and screenshots may leave the guest.' }
    }
} finally { $archive.Dispose() }
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath,$output)
$summary=Get-Content -LiteralPath (Join-Path $output 'summary.json') -Raw | ConvertFrom-Json
if (!$summary.completed) { throw ('Guest tests failed; extracted diagnostics: ' + $output) }
$core=Get-Content -LiteralPath (Join-Path $output 'core.json') -Raw | ConvertFrom-Json
$ui=Get-Content -LiteralPath (Join-Path $output 'ui/results.json') -Raw | ConvertFrom-Json
if (!$summary.completed -or $core.failed -ne 0 -or $core.passed -lt 79 -or $ui.failed -ne 0 -or $ui.passed -ne 123) { throw ('Guest tests need review: ' + $output) }
@{ author='KraicDev'; directory=$output; corePassed=$core.passed; uiPassed=$ui.passed; transferredSha256=$hash; completed=$true } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-final.json') -Encoding UTF8
Write-Output ('Verified guest results: ' + $output)
