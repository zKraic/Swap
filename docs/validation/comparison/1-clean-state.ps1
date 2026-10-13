# Comparison lab step 1: AMD platform (chipset) packages staged like on a Ryzen desktop, then the clean fingerprint F0.
$cd = (Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq 'SWAPLAB').DeviceID
New-Item -ItemType Directory -Force C:\SwapLab\cmp | Out-Null
# Full shutdowns only: resuming a hybrid (fast startup) image hangs under emulation.
'hibernate off: ' + ((& powercfg.exe -h off) -join ' ') + ' exit ' + $LASTEXITCODE
'hiberboot: ' + (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power' -Name HiberbootEnabled -ErrorAction SilentlyContinue).HiberbootEnabled
Copy-Item "$cd\chipset" C:\SwapLab\cmp -Recurse -Force
Copy-Item "$cd\fingerprint.ps1" C:\SwapLab\cmp -Force
foreach ($inf in Get-ChildItem C:\SwapLab\cmp\chipset -Recurse -Filter *.inf) { 'stage ' + $inf.Name + ': ' + ((& pnputil.exe /add-driver $inf.FullName) -join ' ') }
'cpu: ' + (Get-CimInstance Win32_Processor).Name
Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.Class -eq 'Display' } | Format-List InstanceId, FriendlyName, Status, Problem | Out-String
& C:\SwapLab\cmp\fingerprint.ps1 -Out C:\SwapLab\cmp\f0.json
