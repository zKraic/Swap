# KraicDev. Start tests through the private guest agent, in the logged-on guest desktop.
param([switch]$Diagnostic)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
$launch=Get-Content (Join-Path $root 'artifacts/vm/qemu-process.json') -Raw | ConvertFrom-Json
$process=Get-Process -Id $launch.pid -ErrorAction Stop
if (!$config.guestAgent -or $process.Path -ne $config.emulator) { throw 'The private guest agent is not configured or running.' }
$client=[Net.Sockets.TcpClient]::new('127.0.0.1',44443)
try {
    $stream=$client.GetStream(); $stream.ReadTimeout=30000; $stream.WriteTimeout=5000
    $reader=[IO.StreamReader]::new($stream); $writer=[IO.StreamWriter]::new($stream); $writer.AutoFlush=$true
    function Request([string]$name,[hashtable]$arguments=@{}) {
        $requestId=[Guid]::NewGuid().ToString('N')
        $writer.WriteLine((@{ execute=$name; arguments=$arguments; id=$requestId } | ConvertTo-Json -Depth 8 -Compress))
        do { $line=$reader.ReadLine(); if (!$line) { throw 'Guest agent disconnected.' }; $response=$line.TrimStart([char]0xFFFD,[char]255) | ConvertFrom-Json } until ($response.id -eq $requestId)
        if ($response.error) { throw $response.error.desc }
        return $response.return
    }
    $stream.WriteByte(255)
    $null=Request 'guest-sync-delimited' @{ id=[long](Get-Date).Ticks }
    $null=Request 'guest-ping'
    $guestCommand=@'
$ErrorActionPreference='Stop'
try {
if ($env:COMPUTERNAME -ne 'SWAPTESTVM' -or (Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU') { throw 'Not the isolated test guest' }
$account=(Get-CimInstance Win32_ComputerSystem).UserName
if (!$account) { throw 'Guest interactive desktop is not logged on yet' }
$existing=Get-ScheduledTask -TaskName 'KraicDev-Swap-GuestTests' -ErrorAction SilentlyContinue
if($existing -and $existing.State -eq 'Running') { throw 'Guest tests are already running' }
# Let Windows copy executable bytes from the read-only CD; do not trust offline NTFS copies.
$volume=Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq 'SWAPTEST'
if(@($volume).Count -ne 1) { throw 'Expected exactly one read-only test CD' }
$media=$volume.DeviceID+'\bundle'
Copy-Item ($media+'\*') C:\SwapInput -Recurse -Force
$manifest=Get-Content C:\SwapInput\manifest.json -Raw|ConvertFrom-Json
if((Get-FileHash C:\SwapInput\Swap.exe -Algorithm SHA256).Hash -ne $manifest.appSha256 -or (Get-FileHash C:\SwapInput\tests\Swap.Tests.exe -Algorithm SHA256).Hash -ne $manifest.testsSha256) { throw 'Native test media copy checksum mismatch' }
if(Test-Path C:\SwapResults) {
    [void][IO.Directory]::CreateDirectory('C:\SwapRuns')
    Move-Item -LiteralPath C:\SwapResults -Destination ('C:\SwapRuns\'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
}
[void][IO.Directory]::CreateDirectory('C:\SwapResults')
$command="& ([scriptblock]::Create([IO.File]::ReadAllText('C:\SwapInput\Invoke-GuestTests.ps1')))"
$encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
$action=New-ScheduledTaskAction -Execute 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' -Argument ('-NoLogo -NoProfile -WindowStyle Hidden -EncodedCommand '+$encoded)
$principal=New-ScheduledTaskPrincipal -UserId $account -LogonType Interactive -RunLevel Highest
$settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 1) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries
Register-ScheduledTask -TaskName 'KraicDev-Swap-GuestTests' -Action $action -Principal $principal -Settings $settings -Force | Out-Null
Start-ScheduledTask -TaskName 'KraicDev-Swap-GuestTests'
Write-Output 'KraicDev: tests started in the isolated guest desktop.'
} catch {
    if($env:COMPUTERNAME -eq 'SWAPTESTVM'){
        [void][IO.Directory]::CreateDirectory('C:\SwapResults')
        @{author='KraicDev';phase='bootstrap-failed';time=(Get-Date).ToUniversalTime().ToString('o');error=$_.Exception.ToString();position=$_.InvocationInfo.PositionMessage} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath 'C:\SwapResults\diagnostics.json' -Encoding UTF8
    }
    throw
}
'@
    if($Diagnostic){$guestCommand=@'
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -ne 'SWAPTESTVM' -or (Get-CimInstance Win32_ComputerSystem).Manufacturer -ne 'QEMU'){throw 'Not the isolated test guest'}
[void][IO.Directory]::CreateDirectory('C:\SwapResults')
$logon=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon'
@{author='KraicDev';time=(Get-Date).ToUniversalTime().ToString('o');user=(Get-CimInstance Win32_ComputerSystem).UserName;logon=@{user=$logon.DefaultUserName;enabled=$logon.AutoAdminLogon;remaining=$logon.AutoLogonCount};processes=@(Get-Process | Where-Object ProcessName -Match '^(Swap|LogonUI|explorer)' | Select-Object ProcessName,Id,SessionId);testTask=(Get-ScheduledTask -TaskName 'KraicDev-Swap-GuestTests' -ErrorAction SilentlyContinue).State.ToString()} | ConvertTo-Json -Depth 6 | Set-Content 'C:\SwapResults\diagnostics.json' -Encoding UTF8
'@}
    $encoded=[Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($guestCommand))
    $result=Request 'guest-exec' @{ path='C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe'; arg=@('-NoLogo','-NoProfile','-EncodedCommand',$encoded); 'capture-output'=$true }
    @{ author='KraicDev'; pid=$result.pid; started=(Get-Date).ToUniversalTime().ToString('o'); session='Interactive guest administrator'; hostRestart=$false } | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/vm/guest-agent-command.json') -Encoding UTF8
    Start-Sleep -Seconds 3
    $status=Request 'guest-exec-status' @{ pid=$result.pid }
    if ($status.'out-data') { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($status.'out-data')) }
    if ($status.'err-data') { [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($status.'err-data')) }
    if ($status.exited -and $status.exitcode -ne 0) { throw ('Guest bootstrap failed: '+$status.exitcode) }
    Write-Output ('Guest bootstrap PID '+$result.pid+'; read scripts/Read-QemuGuestFile.ps1, then shut down and use scripts/Export-QemuTestResults.ps1.')
} finally { $client.Dispose() }
