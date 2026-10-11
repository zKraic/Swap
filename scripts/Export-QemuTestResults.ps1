# KraicDev. Validate the reports exported from the normally shut-down private guest.
param([string]$ApplicationPath='dist/Swap.exe')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw|ConvertFrom-Json
if(Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue){throw 'Shut down the guest normally first.'}
$dotnet=Join-Path $root 'artifacts/tools/dotnet/dotnet.exe'
if(!(Test-Path $dotnet)){$dotnet='dotnet'}
& $dotnet build (Join-Path $PSScriptRoot 'vm/GuestDiskTool') -c Release --nologo
if($LASTEXITCODE-ne0){throw 'Offline report reader build failed.'}
$tool=Join-Path $PSScriptRoot 'vm/GuestDiskTool/bin/Release/net10.0-windows/GuestDiskTool.dll'
& $dotnet $tool $config.disk extract-results
if($LASTEXITCODE-ne0){throw 'Offline guest report export failed.'}
$output=(Get-ChildItem (Join-Path $root 'artifacts/vm/qemu-results') -Directory|Sort-Object Name -Descending|Select-Object -First 1).FullName
function ReadReport([string]$relative){Get-Content (Join-Path $output $relative) -Raw|ConvertFrom-Json}
$summary=ReadReport 'summary.json';$core=ReadReport 'core.json';$ui=ReadReport 'ui/results.json';$fit=ReadReport 'ui/window-fit.json';$environment=ReadReport 'environment.json'
if(!$summary.completed-or@($summary.checks).Count-ne12-or@($summary.checks|Where-Object{!$_.passed}).Count-ne0-or$core.passed-ne$environment.expectedCorePassed-or$core.passed-lt90-or$core.failed-ne0-or$ui.passed-ne$environment.expectedUiPassed-or$ui.failed-ne0-or!$fit.passed){throw ('Guest reports need review: '+$output)}
$captures=@(Get-ChildItem (Join-Path $output 'ui') -Filter *.png).Count
if($captures-ne$environment.expectedCaptures){throw ('Unexpected count of rendered guest captures; found '+$captures)}
$hash=(Get-FileHash (Join-Path $root $ApplicationPath) -Algorithm SHA256).Hash
if($environment.appSha256-ne$hash-or!$environment.admin-or$environment.preinstalledDotnet){throw 'Guest executable/runtime identity does not match the current delivery.'}
$before=Get-Content (Join-Path $root 'artifacts/vm/qemu-host-display-before.json') -Raw|ConvertFrom-Json
$after=@(Get-CimInstance Win32_PnPSignedDriver|Where-Object DeviceClass -eq 'DISPLAY'|Select-Object DeviceID,InfName,DriverVersion)
$beforeJson=ConvertTo-Json -InputObject @($before) -Depth 5 -Compress
$afterJson=ConvertTo-Json -InputObject $after -Depth 5 -Compress
if($beforeJson-ne$afterJson){throw 'Host active display inventory changed; review before claiming success.'}
@{author='KraicDev';directory=$output;corePassed=$core.passed;uiPassed=$ui.passed;checksPassed=@($summary.checks).Count;captures=$captures;windowFits=$fit.passed;appSha256=$hash;completed=$true;guestOs=$environment.os;guestVersion=$environment.version;hostDisplayUnchanged=$true;hostRestart=$false;exportMethod='Offline read after normal guest shutdown'}|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/vm/qemu-final.json') -Encoding UTF8
$after|ConvertTo-Json -Depth 5|Set-Content (Join-Path $root 'artifacts/vm/qemu-host-display-after.json') -Encoding UTF8
Write-Output ('KraicDev: current EXE verified in isolated Windows: '+$output)

