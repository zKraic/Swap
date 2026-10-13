# KraicDev comparison lab. Read-only fingerprint of the guest state that GPU cleanup tools touch.
param([Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = 'Continue'; $ProgressPreference = 'SilentlyContinue'
if ($env:COMPUTERNAME -ne 'SWAPTESTVM') { throw 'lab only' }
$r = [ordered]@{ time = (Get-Date).ToString('o') }
$r.packages = @(Get-WindowsDriver -Online | ForEach-Object { '{0}|{1}|{2}|{3}' -f [IO.Path]::GetFileName($_.OriginalFileName), $_.ProviderName, $_.ClassName, $_.Version } | Sort-Object)
$r.storeFolders = @(Get-ChildItem C:\Windows\System32\DriverStore\FileRepository -Directory -Force | ForEach-Object Name | Sort-Object)
$devices = New-Object System.Collections.Generic.List[string]
$enum = [Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SYSTEM\CurrentControlSet\Enum')
foreach ($bus in $enum.GetSubKeyNames()) {
    try { $b = $enum.OpenSubKey($bus) } catch { continue }; if (!$b) { continue }
    foreach ($model in $b.GetSubKeyNames()) {
        try { $m = $b.OpenSubKey($model) } catch { continue }; if (!$m) { continue }
        foreach ($instance in $m.GetSubKeyNames()) { $devices.Add("$bus\$model\$instance") }
    }
}
$r.devices = @($devices | Sort-Object)
$r.presentDevices = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | ForEach-Object InstanceId | Sort-Object)
$r.services = @(Get-ChildItem HKLM:\SYSTEM\CurrentControlSet\Services | ForEach-Object { $_.PSChildName + '|' + [string]$_.GetValue('ImagePath', '', 'DoNotExpandEnvironmentNames') } | Sort-Object)
$exclude = @('C:\Windows\System32\DriverStore', 'C:\Windows\System32\config', 'C:\Windows\System32\LogFiles', 'C:\Windows\System32\winevt', 'C:\Windows\System32\wbem\Repository',
    'C:\Windows\System32\sru', 'C:\Windows\System32\catroot2', 'C:\Windows\System32\spool', 'C:\Windows\System32\Tasks', 'C:\Windows\System32\WDI', 'C:\Windows\System32\SleepStudy',
    'C:\Windows\System32\Microsoft\Protect', 'C:\Windows\System32\CodeIntegrity', 'C:\Windows\System32\Tasks_Migrated', 'C:\Windows\SysWOW64\config', 'C:\Windows\System32\SMI\Store')
function Files([string]$root) {
    Get-ChildItem $root -Recurse -File -Force -ErrorAction SilentlyContinue | Where-Object { $p = $_.FullName; -not ($exclude | Where-Object { $p.StartsWith($_ + '\', [StringComparison]::OrdinalIgnoreCase) }) } |
        ForEach-Object { $_.FullName.Substring(11) + '|' + $_.Length }
}
$r.system32 = @(Files 'C:\Windows\System32' | Sort-Object)
$r.syswow64 = @(Files 'C:\Windows\SysWOW64' | Sort-Object)
$r.programDirs = @(foreach ($root in 'C:\Program Files', 'C:\Program Files (x86)', 'C:\ProgramData') { Get-ChildItem $root -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName; Get-ChildItem $_.FullName -Directory -Force -ErrorAction SilentlyContinue | ForEach-Object FullName } })
function Dump([string]$path, [switch]$NamesOnly) {
    if (!(Test-Path -LiteralPath $path)) { return }
    $keys = @(Get-Item -LiteralPath $path) + @(Get-ChildItem -LiteralPath $path -Recurse -ErrorAction SilentlyContinue)
    foreach ($k in $keys) {
        $names = @($k.GetValueNames())
        if ($NamesOnly -or $names.Count -eq 0) { $k.Name + '|'; continue }
        foreach ($n in $names) { $k.Name + '|' + $n + '=' + ((@($k.GetValue($n, $null, 'DoNotExpandEnvironmentNames')) | ForEach-Object { [string]$_ }) -join ';') }
    }
}
$r.registry = @(
    Dump 'HKLM:\SOFTWARE\Khronos'; Dump 'HKLM:\SOFTWARE\WOW6432Node\Khronos'
    Dump 'HKLM:\SOFTWARE\AMD'; Dump 'HKLM:\SOFTWARE\WOW6432Node\AMD'; Dump 'HKLM:\SOFTWARE\ATI Technologies'; Dump 'HKLM:\SOFTWARE\WOW6432Node\ATI Technologies'; Dump 'HKLM:\SOFTWARE\ATI'
    Dump 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}'
    Dump 'HKLM:\SYSTEM\CurrentControlSet\Control\Video' -NamesOnly
    Dump 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'; Dump 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'; Dump 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce'
    Dump 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles'
    Dump 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Drivers32'
    Dump 'HKLM:\SOFTWARE\Classes\Directory\Background\shellex\ContextMenuHandlers'
    Dump 'HKLM:\SYSTEM\DriverDatabase\DriverPackages' -NamesOnly
    foreach ($u in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall') {
        Get-ChildItem $u -ErrorAction SilentlyContinue | ForEach-Object { $_.Name + '|DisplayName=' + [string]$_.GetValue('DisplayName') } }
)
$r.deviceIds = @(Get-ChildItem 'HKLM:\SYSTEM\DriverDatabase\DeviceIds\PCI' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match 'VEN_(1002|1022)' } |
    ForEach-Object { $k = $_; @($k.GetValueNames()) | ForEach-Object { $k.PSChildName + '|' + $_ } })
$r.com = @(Get-ChildItem HKLM:\SOFTWARE\Classes\CLSID -ErrorAction SilentlyContinue | ForEach-Object {
    foreach ($server in 'InprocServer32', 'LocalServer32') { $s = $_.OpenSubKey($server); if ($s) { $_.PSChildName + '|' + $server + '=' + [string]$s.GetValue('', '', 'DoNotExpandEnvironmentNames'); $s.Close() } } } | Sort-Object)
$r.tasks = @(Get-ScheduledTask -ErrorAction SilentlyContinue | ForEach-Object { $_.TaskPath + $_.TaskName } | Sort-Object)
$r.appx = @(try { Get-AppxPackage -AllUsers -ErrorAction Stop | ForEach-Object Name | Sort-Object -Unique } catch { 'unavailable' })
$r.path = [Environment]::GetEnvironmentVariable('Path', 'Machine')
# System behaviour a cleanup tool may change besides removing files: driver search policy, safe-mode boot, startup entries.
$r.policy = @(
    Dump 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching'
    Dump 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DriverSearching'
    Dump 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate'
    Dump 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeviceInstall'
    Dump 'HKLM:\SYSTEM\CurrentControlSet\Control\SafeBoot' -NamesOnly
    Dump 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce'
    'bcd|' + ((& bcdedit.exe /enum '{current}') -match 'safeboot|bootstatus' -join ';')
)
[IO.File]::WriteAllText($Out, ($r | ConvertTo-Json -Depth 3 -Compress), [Text.UTF8Encoding]::new($false))
'fingerprint ' + $Out + ' ' + (Get-Item $Out).Length
