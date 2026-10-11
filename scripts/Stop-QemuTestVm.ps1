# KraicDev - requests a normal ACPI shutdown of this project's guest, never the host.
param()
$ErrorActionPreference = 'Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
$launch=Get-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-process.json') -Raw | ConvertFrom-Json
$guestProcess=Get-Process -Id $launch.pid -ErrorAction SilentlyContinue
if (!$guestProcess) { Write-Output 'The test guest is already stopped.'; exit 0 }
if ($guestProcess.Path -ne $config.emulator) { throw 'The recorded process does not belong to the test emulator.' }
$client=[Net.Sockets.TcpClient]::new('127.0.0.1',44442)
try {
    $stream=$client.GetStream(); $stream.ReadTimeout=5000; $stream.WriteTimeout=5000
    $reader=[IO.StreamReader]::new($stream)
    $writer=[IO.StreamWriter]::new($stream); $writer.AutoFlush=$true
    $greeting=$reader.ReadLine() | ConvertFrom-Json
    if (!$greeting.QMP) { throw 'No QMP greeting from the test emulator.' }
    function Invoke-Qmp([string]$command,[string]$requestId) {
        @{ execute=$command; id=$requestId } | ConvertTo-Json -Compress | ForEach-Object { $writer.WriteLine($_) }
        do { $line=$reader.ReadLine(); if (!$line) { throw 'QMP connection closed.' }; $response=$line | ConvertFrom-Json } until ($response.id -eq $requestId)
        if ($response.error) { throw $response.error.desc }
        return $response.return
    }
    $null=Invoke-Qmp 'qmp_capabilities' 'capabilities'
    $identity=Invoke-Qmp 'query-name' 'identity'
    if ($identity.name -ne 'Swap - KraicDev test VM') { throw 'The connected VM is not the Swap test guest.' }
    $null=Invoke-Qmp 'system_powerdown' 'shutdown'
} finally { $client.Dispose() }
Write-Output 'Normal shutdown requested for the test guest. No forced termination or host restart.'
