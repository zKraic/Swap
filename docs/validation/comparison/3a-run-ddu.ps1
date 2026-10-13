# Comparison lab: official DDU (Wagnardsoft, signed) with its default AMD cleanup in normal mode, as SYSTEM, no restart.
$cd = (Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq 'SWAPLAB').DeviceID
$installer = Get-ChildItem "$cd\ddu" -Filter *.exe | Select-Object -First 1
$sig = Get-AuthenticodeSignature $installer.FullName
'installer ' + $installer.Name + ' ' + $sig.Status + ' ' + $sig.SignerCertificate.Subject + ' ' + (Get-FileHash $installer.FullName).Hash
$p = Start-Process $installer.FullName -ArgumentList '/S' -Wait -PassThru; 'install exit ' + $p.ExitCode
$exe = Get-ChildItem 'C:\Program Files\Display Driver Uninstaller', 'C:\Program Files (x86)\Display Driver Uninstaller', 'C:\Program Files\Wagnardsoft' -Filter 'Display Driver Uninstaller.exe' -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
if (!$exe) { $exe = Get-ChildItem C:\ -Filter 'Display Driver Uninstaller.exe' -Recurse -Depth 4 -ErrorAction SilentlyContinue | Select-Object -First 1 }
'ddu ' + $exe.FullName + ' ' + $exe.VersionInfo.ProductVersion
$watch = [Diagnostics.Stopwatch]::StartNew()
$run = Start-Process $exe.FullName -ArgumentList '-Silent', '-CleanAmd', '-NoSafeModeMsg' -PassThru -WorkingDirectory (Split-Path $exe.FullName)
if (!$run.WaitForExit(2400000)) { 'ddu still running after 40 min'; Get-Process | Where-Object Path -like '*Display Driver Uninstaller*' | Format-Table Id, Path, StartTime -AutoSize | Out-String }
else { 'ddu exit ' + $run.ExitCode + ' after ' + [int]$watch.Elapsed.TotalSeconds + ' s' }
$logs = Get-ChildItem (Split-Path $exe.FullName) -Recurse -Include *.log, *.txt -ErrorAction SilentlyContinue | Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-2) }
foreach ($log in $logs) { '== ' + $log.FullName + ' ' + $log.Length; Copy-Item $log.FullName C:\SwapLab\cmp\ -Force; Get-Content $log.FullName -Tail 40 }
'restore points: ' + (@(Get-ComputerRestorePoint -ErrorAction SilentlyContinue) | ForEach-Object Description) -join '; '
