$ErrorActionPreference = 'Stop'
$warnings = @()
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$admin = ([Security.Principal.WindowsPrincipal]$identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$os = Get-CimInstance Win32_OperatingSystem
$system = Get-CimInstance Win32_ComputerSystem
$chassis = Get-CimInstance Win32_SystemEnclosure
# Filter in WMI: enumerating every signed driver first takes minutes on slow PCs.
$signed = @(Get-CimInstance Win32_PnPSignedDriver -Filter "DeviceClass='DISPLAY'")
$devices = @(Get-PnpDevice -Class Display -PresentOnly -ErrorAction Stop)
$adapters = @($devices | ForEach-Object {
    $device = $_
    $driver = $signed | Where-Object { $_.DeviceID -eq $device.InstanceId } | Select-Object -First 1
    $vendor = 0
    if ($device.InstanceId -match 'VEN_10DE') { $vendor = 1 }
    elseif ($device.InstanceId -match 'VEN_1002') { $vendor = 2 }
    elseif ($device.InstanceId -match 'VEN_8086') { $vendor = 3 }
    $problem = Get-PnpDeviceProperty -InstanceId $device.InstanceId -KeyName 'DEVPKEY_Device_ProblemCode' -ErrorAction Stop
    @{ Name=[string]$device.FriendlyName; Id=[string]$device.InstanceId; Vendor=$vendor;
       Inf=[string]$driver.InfName; Version=[string]$driver.DriverVersion; Problem=[int]$problem.Data; Signed=[bool]$driver.IsSigned }
})
# Disconnected display adapters (for example the card that was removed) still own their driver package.
$phantoms = @(Get-PnpDevice -Class Display -ErrorAction SilentlyContinue | Where-Object { -not $_.Present } | ForEach-Object {
    $vendor = 0
    if ($_.InstanceId -match 'VEN_10DE') { $vendor = 1 } elseif ($_.InstanceId -match 'VEN_1002') { $vendor = 2 } elseif ($_.InstanceId -match 'VEN_8086') { $vendor = 3 }
    $inf = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName 'DEVPKEY_Device_DriverInfPath' -ErrorAction SilentlyContinue).Data
    @{ Name=[string]$_.FriendlyName; Id=[string]$_.InstanceId; Vendor=$vendor; Inf=[string]$inf; Version=''; Problem=0; Signed=$false }
})
$cpu = Get-ItemProperty -LiteralPath 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' -ErrorAction SilentlyContinue
$complete = $false
$drivers = @()
if ($admin) {
    $drivers = @(Get-WindowsDriver -Online -ErrorAction Stop | Where-Object { $_.ClassName -eq 'Display' } | ForEach-Object {
        @{ Inf=[string]$_.Driver; Provider=[string]$_.ProviderName; ClassGuid='{4d36e968-e325-11ce-bfc1-08002be10318}';
           Version=[string]$_.Version; BootCritical=[bool]$_.BootCritical }
    })
    $complete = $true
} else { $warnings += 'Inventario del almacén pendiente: abre con permisos de administrador para preparar un cambio.' }
$pending = (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') -or
           (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired')
$rename = Get-ItemProperty -LiteralPath 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name PendingFileRenameOperations -ErrorAction SilentlyContinue
# Pending deletions (installer rollback files, old copies) need no restart before a driver change; a pending replacement does.
$operations = @($rename.PendingFileRenameOperations)
for ($i = 0; $i + 1 -lt $operations.Count; $i += 2) { if ($operations[$i + 1]) { $pending = $true } }
# Some boards report an all-zero or all-F UUID; the Windows installation GUID then identifies this PC.
$machine = [string](Get-CimInstance Win32_ComputerSystemProduct).UUID
if (!$machine -or $machine -match '^[0F-]+$') { $machine = 'WIN-' + [string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).MachineGuid }
$disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($env:SystemDrive)'"
@{ Os=[string]($os.Caption + ' · ' + $os.Version); MachineId=$machine;
   Admin=$admin; Complete=$complete; PendingReboot=[bool]$pending; Remote=[bool]($env:SESSIONNAME -like 'RDP*');
   Laptop=[bool](@($chassis.ChassisTypes | Where-Object { $_ -in 8,9,10,14,30,31,32 }).Count -gt 0 -or $system.PCSystemType -eq 2);
   FreeBytes=[long]$disk.FreeSpace; Adapters=$adapters; Drivers=$drivers; Warnings=@($warnings); BootId=$os.LastBootUpTime.ToUniversalTime().ToString('o');
   Phantoms=$phantoms; CpuVendor=[string]$cpu.VendorIdentifier; CpuName=([string]$cpu.ProcessorNameString).Trim() } | ConvertTo-Json -Depth 8 -Compress
