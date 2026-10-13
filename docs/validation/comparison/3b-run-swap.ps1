# Comparison lab: Swap 0.6 cleanup of the removed AMD card with the default selection (what the app runs automatically).
$cd = (Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq 'SWAPLAB').DeviceID
New-Item -ItemType Directory -Force C:\SwapLab\swap | Out-Null
Copy-Item "$cd\tests\Swap.Tests.exe" C:\SwapLab\swap\ -Force
'tests ' + (Get-FileHash C:\SwapLab\swap\Swap.Tests.exe).Hash
$watch = [Diagnostics.Stopwatch]::StartNew()
& C:\SwapLab\swap\Swap.Tests.exe '--amd-cleanup-lab=C:\SwapLab\swap\out' '--api-probe-readonly' '--report=C:\SwapLab\swap\core.json' *> C:\SwapLab\swap\swap.log
'exit ' + $LASTEXITCODE + ' after ' + [int]$watch.Elapsed.TotalSeconds + ' s'
Get-Content C:\SwapLab\swap\swap.log | Select-String -Pattern '^(plan|kept|result|inventory|Comparison|FAIL|RESULT|API probe)|^  (d3d12|vulkan|opencl)' | ForEach-Object Line
