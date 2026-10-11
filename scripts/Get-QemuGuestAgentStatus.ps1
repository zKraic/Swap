# KraicDev. Read the result of the last bootstrap process inside the private VM.
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
$launch=Get-Content (Join-Path $root 'artifacts/vm/qemu-process.json') -Raw | ConvertFrom-Json
$command=Get-Content (Join-Path $root 'artifacts/vm/guest-agent-command.json') -Raw | ConvertFrom-Json
if ((Get-Process -Id $launch.pid -ErrorAction Stop).Path -ne $config.emulator) { throw 'Unexpected emulator process.' }
$client=[Net.Sockets.TcpClient]::new('127.0.0.1',44443)
try {
    $stream=$client.GetStream(); $stream.ReadTimeout=15000; $stream.WriteTimeout=5000
    $reader=[IO.StreamReader]::new($stream); $writer=[IO.StreamWriter]::new($stream); $writer.AutoFlush=$true
    function Request([string]$name,[hashtable]$arguments) {
        $requestId=[Guid]::NewGuid().ToString('N')
        $writer.WriteLine((@{ execute=$name; arguments=$arguments; id=$requestId } | ConvertTo-Json -Compress))
        do { $line=$reader.ReadLine(); if (!$line) { throw 'Guest disconnected.' }; $response=$line.TrimStart([char]0xFFFD,[char]255) | ConvertFrom-Json } until ($response.id -eq $requestId)
        if ($response.error) { throw $response.error.desc }
        return $response.return
    }
    $stream.WriteByte(255)
    $null=Request 'guest-sync-delimited' @{ id=[long](Get-Date).Ticks }
    $status=Request 'guest-exec-status' @{ pid=$command.pid }
    Write-Host ($status | Select-Object exited,exitcode,signal | ConvertTo-Json)
    if ($status.'out-data') { Write-Host ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($status.'out-data'))) }
    if ($status.'err-data') { Write-Host ([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($status.'err-data'))) }
    if ($status.exited -and ($status.exitcode -ne 0 -or $null -ne $status.signal)) { throw ('Guest bootstrap failed: exit='+$status.exitcode+' signal='+$status.signal) }
} finally { $client.Dispose() }
