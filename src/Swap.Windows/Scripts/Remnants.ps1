# KraicDev. Read-only remnant discovery for the parts that need Windows PowerShell modules.
$ErrorActionPreference = 'Stop'
$warnings = @()
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
$system = Get-CimInstance Win32_ComputerSystem
$chassis = Get-CimInstance Win32_SystemEnclosure
$packages = @(); $complete = $false
if ($admin) {
    $packages = @(Get-WindowsDriver -Online -ErrorAction Stop | ForEach-Object {
        @{ Inf=[string]$_.Driver; Original=[string]$_.OriginalFileName; Provider=[string]$_.ProviderName; Class=[string]$_.ClassName; Version=[string]$_.Version; BootCritical=[bool]$_.BootCritical } })
    $complete = $true
} else { $warnings += 'Administrator permission is required for the driver store.' }
$tasks = @()
try {
    $tasks = @(Get-ScheduledTask -ErrorAction Stop | ForEach-Object {
        @{ Path=[string]($_.TaskPath + $_.TaskName); Actions=@($_.Actions | Where-Object { $_.Execute } | ForEach-Object { ([string]$_.Execute + ' ' + [string]$_.Arguments).Trim() }) } })
} catch { $warnings += 'Scheduled tasks unavailable.'; $complete = $false }
# Some boards report an all-zero or all-F UUID; the Windows installation GUID then identifies this PC.
$machine = [string](Get-CimInstance Win32_ComputerSystemProduct).UUID
if (!$machine -or $machine -match '^[0F-]+$') { $machine = 'WIN-' + [string](Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Cryptography' -Name MachineGuid).MachineGuid }
$appx = @()
if ($admin) {
    $names = @('__APPX__' | ConvertFrom-Json)
    try {
        $appx = @(Get-AppxPackage -AllUsers -ErrorAction Stop | Where-Object { $_.Name -in $names } | ForEach-Object { [string]$_.Name })
        $appx += @(Get-AppxProvisionedPackage -Online -ErrorAction Stop | Where-Object { $_.DisplayName -in $names } | ForEach-Object { [string]$_.DisplayName })
    } catch { $warnings += 'Store app inventory unavailable.' }
}
@{ Admin=$admin; Complete=$complete; MachineId=$machine;
   Laptop=[bool](@($chassis.ChassisTypes | Where-Object { $_ -in 8,9,10,14,30,31,32 }).Count -gt 0 -or $system.PCSystemType -eq 2);
   Packages=$packages; Tasks=$tasks; Appx=@($appx | Select-Object -Unique); Warnings=@($warnings) } | ConvertTo-Json -Depth 6 -Compress
