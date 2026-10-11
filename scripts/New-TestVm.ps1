# KraicDev - creates a reproducible, offline Windows Sandbox test configuration.
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bundle = Join-Path $root 'artifacts/vm/bundle'
$output = Join-Path $root ('artifacts/vm/runs/' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
[void][IO.Directory]::CreateDirectory($bundle)
[void][IO.Directory]::CreateDirectory($output)
if (!(Test-Path -LiteralPath (Join-Path $bundle 'tests/Swap.Tests.exe'))) { throw 'Publish the self-contained test executable before creating the VM configuration.' }
Copy-Item -LiteralPath (Join-Path $root 'dist/Swap.exe') -Destination (Join-Path $bundle 'Swap.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-GuestTests.ps1') -Destination (Join-Path $bundle 'Invoke-GuestTests.ps1') -Force
@{ expectedCorePassed=([int](Get-Content (Join-Path $root 'artifacts/test-results.json') -Raw|ConvertFrom-Json).passed+3); expectedUiPassed=126; expectedCaptures=33; author='KraicDev'; hostComputer=$env:COMPUTERNAME; hostUuid=[string](Get-CimInstance Win32_ComputerSystemProduct).UUID;
    appSha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'Swap.exe') -Algorithm SHA256).Hash;
    testsSha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'tests/Swap.Tests.exe') -Algorithm SHA256).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Encoding UTF8
$escapedInput = [Security.SecurityElement]::Escape($bundle)
$escapedOutput = [Security.SecurityElement]::Escape($output)
# Execute the local script body without changing execution policy. No network or host GPU sharing.
$command = 'powershell.exe -NoLogo -NoProfile -Command "&amp; ([scriptblock]::Create([IO.File]::ReadAllText(''C:\SwapInput\Invoke-GuestTests.ps1'')))"'
$config = @"
<Configuration>
 <VGpu>Disable</VGpu><Networking>Disable</Networking><MemoryInMB>4096</MemoryInMB>
 <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput>
 <PrinterRedirection>Disable</PrinterRedirection><ClipboardRedirection>Disable</ClipboardRedirection>
 <MappedFolders>
  <MappedFolder><HostFolder>$escapedInput</HostFolder><SandboxFolder>C:\SwapInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
  <MappedFolder><HostFolder>$escapedOutput</HostFolder><SandboxFolder>C:\SwapResults</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
 </MappedFolders>
 <LogonCommand><Command>$command</Command></LogonCommand>
</Configuration>
"@
$path = Join-Path $output 'Swap-Tests.wsb'
$config | Set-Content -LiteralPath $path -Encoding UTF8
@{ configuration=$path; output=$output; bundle=$bundle; appSha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'Swap.exe') -Algorithm SHA256).Hash } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/vm/current-run.json') -Encoding UTF8
Write-Output $path

