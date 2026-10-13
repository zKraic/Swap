# Comparison lab: registry areas the main fingerprint does not cover (key names only). Writes C:\SwapLab\cmp\<name>-extras.json.
param([string]$Name = '__NAME__')
if ($env:COMPUTERNAME -ne 'SWAPTESTVM') { throw 'lab only' }
function Keys([string]$path) {
    if (!(Test-Path -LiteralPath $path)) { return }
    Get-ChildItem -LiteralPath $path -Recurse -ErrorAction SilentlyContinue | ForEach-Object { $_.Name }
}
$r = [ordered]@{}
$r.mft = @(Keys 'HKLM:\SOFTWARE\Classes\MediaFoundation\Transforms'; Keys 'HKLM:\SOFTWARE\Classes\WOW6432Node\MediaFoundation\Transforms')
$r.pnpResources = @(Keys 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpResources'; Keys 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Setup\PnpResources')
$r.lockdown = @((Get-Item 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Setup\PnpLockdownFiles').GetSubKeyNames())
$r.clsid = @(Get-ChildItem HKLM:\SOFTWARE\Classes\CLSID, HKLM:\SOFTWARE\WOW6432Node\Classes\CLSID -ErrorAction SilentlyContinue | ForEach-Object Name)
$r.classKeys = @(Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}' -ErrorAction SilentlyContinue | ForEach-Object Name)
New-Item -ItemType Directory -Force C:\SwapLab\cmp | Out-Null
$out = "C:\SwapLab\cmp\$Name-extras.json"
[IO.File]::WriteAllText($out, ($r | ConvertTo-Json -Depth 3 -Compress), [Text.UTF8Encoding]::new($false))
'extras ' + $out + ' ' + (Get-Item $out).Length + ' mft=' + $r.mft.Count + ' pnpResources=' + $r.pnpResources.Count + ' lockdown=' + $r.lockdown.Count
