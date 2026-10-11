# KraicDev - resumes the prepared VM after the Windows feature installation/restart.
param([int]$TimeoutSeconds = 900)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$run = Get-Content -LiteralPath (Join-Path $root 'artifacts/vm/current-run.json') -Raw | ConvertFrom-Json
if (!(Test-Path -LiteralPath $run.configuration)) { throw 'Prepare the VM with New-TestVm.ps1 first.' }
if (Test-Path -LiteralPath (Join-Path $run.output 'status.json')) { throw 'This run already has results. Generate a fresh run with New-TestVm.ps1 before launching again.' }
$baseline = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceClass -eq 'DISPLAY' | Select-Object DeviceID,InfName,DriverVersion)
$baseline | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run.output 'host-display-before.json') -Encoding UTF8
Start-Process -FilePath $run.configuration -WindowStyle Hidden
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$lastPhase = ''
do {
    Start-Sleep -Seconds 5
    $statusPath = Join-Path $run.output 'status.json'
    if (!(Test-Path -LiteralPath $statusPath)) { continue }
    try { $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json } catch { continue }
    if ($status.phase -ne $lastPhase) { Write-Output ($status.phase + ': ' + $status.detail); $lastPhase = $status.phase }
    if ($status.phase -eq 'failed') { throw ('Guest tests failed: ' + $status.detail) }
    if ($status.phase -eq 'completed') {
        $after = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceClass -eq 'DISPLAY' | Select-Object DeviceID,InfName,DriverVersion)
        $after | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run.output 'host-display-after.json') -Encoding UTF8
        $unchanged = ($baseline | ConvertTo-Json -Depth 5 -Compress) -eq ($after | ConvertTo-Json -Depth 5 -Compress)
        @{ author='KraicDev'; activeHostDriversUnchanged=$unchanged; guestOutput=$run.output; time=(Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run.output 'host-verification.json') -Encoding UTF8
        if (!$unchanged) { throw 'Host display inventory changed during testing; review the snapshots before attributing the change.' }
        Write-Output ('VM tests complete. Reports: ' + $run.output)
        exit 0
    }
} while ((Get-Date) -lt $deadline)
throw ('No completed guest report within the timeout. Inspect Windows Sandbox and ' + $run.output + '. No VM or servicing process was killed.')
