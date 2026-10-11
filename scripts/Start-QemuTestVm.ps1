# KraicDev - starts only the private virtual disk and read-only CDs, with networking disabled.
param([ValidateSet('whpx','tcg')][string]$Accelerator='tcg', [switch]$Resume, [switch]$Headless,
    [ValidateRange(1,8)][int]$CpuCount=2, [ValidateRange(2048,8192)][int]$MemoryInMb=3072)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Get-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
if (Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue) { throw 'A QEMU instance is already running. Close the test guest before starting another.' }
foreach ($path in @($config.emulator,$config.disk,$config.payload,$config.windowsIso)) { if (!(Test-Path -LiteralPath $path)) { throw ('VM input missing: ' + $path) } }
$rootPath = [IO.Path]::GetFullPath($config.root).TrimEnd('\') + '\'
foreach ($path in @($config.disk,$config.payload,$config.windowsIso,$config.serialLog)) {
    if (![IO.Path]::GetFullPath($path).StartsWith($rootPath,[StringComparison]::OrdinalIgnoreCase)) { throw 'All virtual media must be within the dedicated VM directory.' }
}
if (!$Resume -and (Get-Item -LiteralPath $config.disk).Length -gt 1MB) {
    throw 'This virtual disk is no longer empty. Use -Resume to preserve the installed guest; create a new VM for a new installation.'
}
$windowsHash = (Get-FileHash -LiteralPath $config.windowsIso -Algorithm SHA256).Hash
if ($windowsHash -ne 'E4AB2E3535BE5748252A8D5D57539A6E59BE8D6726345EE10E7AFD2CB89FEFB5') { throw 'Windows evaluation ISO does not match the Microsoft hash.' }
$firmware = Join-Path (Split-Path $config.emulator -Parent) 'share'
$acceleratorOption = if ($Accelerator -eq 'tcg') { 'tcg,thread=multi' } else { 'whpx' }
# Software IRQ routing avoids the WHPX guest reset failure seen on this Windows host.
$machineOption = if ($Accelerator -eq 'whpx') { 'pc,kernel-irqchip=off' } else { 'pc' }
$diskFormat = switch ([IO.Path]::GetExtension($config.disk)) { '.vhd' {'vpc'} '.img' {'raw'} default {'qcow2'} }
$qemuArgs = @('-name','Swap - KraicDev test VM','-L',$firmware,'-machine',$machineOption,'-accel',$acceleratorOption,'-cpu','qemu64,-svm','-smp',[string]$CpuCount,'-m',[string]$MemoryInMb,'-rtc','base=localtime',
    '-drive',('file=' + $config.disk.Replace('\','/') + ',format=' + $diskFormat + ',if=ide,index=0'),
    '-drive',('file=' + $config.windowsIso.Replace('\','/') + ',media=cdrom,if=ide,index=2,readonly=on'),
    '-drive',('file=' + $config.payload.Replace('\','/') + ',media=cdrom,if=ide,index=3,readonly=on'),
    '-nic','none','-vga','std','-display',$(if($Headless){'none'}else{'gtk'}),'-device','qemu-xhci,id=swap-usb',
    '-device','usb-tablet,bus=swap-usb.0,id=swap-pointer','-serial',('file:' + $config.serialLog.Replace('\','/')),
    '-qmp','tcp:127.0.0.1:44442,server=on,wait=off', '-boot', $(if ($Resume) { 'order=c' } else { 'order=c,once=d' }))
if ($config.guestAgent) { $qemuArgs += @('-serial','tcp:127.0.0.1:44443,server=on,wait=off') }
# Start-Process owns file redirection even after this launcher returns; no abandoned stdout pipes.
if (@($qemuArgs | Where-Object { $_.Contains('"') }).Count -gt 0) { throw 'VM arguments cannot contain embedded quotes.' }
$argumentLine = ($qemuArgs | ForEach-Object { '"' + $_ + '"' }) -join ' '
$stderr = Join-Path $config.root 'qemu-stderr.log'
# This is the interactive guest display, required for observing installation and app tests.
$process = Start-Process -FilePath $config.emulator -ArgumentList $argumentLine -WorkingDirectory (Split-Path $config.emulator -Parent) -WindowStyle $(if($Headless){'Hidden'}else{'Normal'}) -RedirectStandardOutput (Join-Path $config.root 'qemu-stdout.log') -RedirectStandardError $stderr -PassThru
@{ author='KraicDev'; pid=$process.Id; accelerator=$Accelerator; started=(Get-Date).ToUniversalTime().ToString('o'); hostRestart=$false; network=$false;
    windowsIsoSha256=$windowsHash; arguments=$qemuArgs; vmRoot=$config.root } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-process.json') -Encoding UTF8
if ($process.WaitForExit(3000)) { throw ('QEMU failed: ' + (Get-Content -LiteralPath $stderr -Raw)) }
Write-Output ('QEMU VM running; PID ' + $process.Id + '; ' + $Accelerator)
