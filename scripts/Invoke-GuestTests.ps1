# KraicDev - run only inside the isolated Sandbox or QEMU guest prepared by this project.
param([switch]$SerialReports,[switch]$Preview)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$results = [Collections.Generic.List[object]]::new()
$completed = $false
$serial = $null
function Save-GuestStatus([string]$phase, [string]$detail='') {
    $status = @{ author='KraicDev'; phase=$phase; detail=$detail; time=(Get-Date).ToUniversalTime().ToString('o'); results=$results }
    $status | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath C:/SwapResults/status.json -Encoding UTF8
    if ($null -ne $serial -and $serial.IsOpen) { $serial.WriteLine('SWAP_STATUS ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($status | ConvertTo-Json -Depth 10 -Compress)))) }
}
function Check([string]$name, [bool]$passed, [string]$detail='') {
    $results.Add(@{ name=$name; passed=$passed; detail=$detail })
    Save-GuestStatus 'running' $name
    if (!$passed) { throw ($name + ': ' + $detail) }
}
try {
    $guestSystem = Get-CimInstance Win32_ComputerSystem
    $isSandbox = $env:USERNAME -eq 'WDAGUtilityAccount'
    $isQemu = $env:COMPUTERNAME -eq 'SWAPTESTVM' -and $guestSystem.Manufacturer -eq 'QEMU'
    if ((!$isSandbox -and !$isQemu) -or !(Test-Path -LiteralPath C:/SwapInput/manifest.json) -or !(Test-Path -LiteralPath C:/SwapResults)) {
        throw 'This runner is only for the isolated test VM. It must not run on the host.'
    }
    if ($isQemu -and $SerialReports) { $serial = New-Object System.IO.Ports.SerialPort 'COM1',115200,'None',8,'One'; $serial.WriteTimeout=30000; $serial.Open() }
    Save-GuestStatus 'starting' 'Isolated guest started.'
    $manifest = Get-Content -LiteralPath C:/SwapInput/manifest.json -Raw | ConvertFrom-Json
    $guestUuid = [string](Get-CimInstance Win32_ComputerSystemProduct).UUID
    Check 'Guest is a different Windows instance' ($env:COMPUTERNAME -ne $manifest.hostComputer -and $guestUuid -ne $manifest.hostUuid) $guestSystem.Model
    $principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
    $guestAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $guestOs = Get-CimInstance Win32_OperatingSystem
    $displayBefore = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceClass -eq 'DISPLAY' | Select-Object DeviceID,InfName,DriverVersion)
    $storeBefore = @(Get-WindowsDriver -Online | Where-Object ClassName -eq 'Display' | Select-Object Driver,ProviderName,Version)
    @{ os=$guestOs.Caption; version=$guestOs.Version; computer=$env:COMPUTERNAME; uuid=$guestUuid; model=$guestSystem.Model; admin=$guestAdmin;
        appSha256=$manifest.appSha256; testsSha256=$manifest.testsSha256; expectedCorePassed=$manifest.expectedCorePassed; expectedUiPassed=$manifest.expectedUiPassed; expectedCaptures=$manifest.expectedCaptures;
        preinstalledDotnet=(Test-Path -LiteralPath 'C:/Program Files/dotnet/dotnet.exe'); display=$displayBefore; store=$storeBefore } |
        ConvertTo-Json -Depth 6 | Set-Content -LiteralPath C:/SwapResults/environment.json -Encoding UTF8
    Check 'Guest administrator and native inventory' $guestAdmin
    Check 'Portable app checksum' ((Get-FileHash -LiteralPath C:/SwapInput/Swap.exe -Algorithm SHA256).Hash -eq $manifest.appSha256)
    Check 'Portable tests checksum' ((Get-FileHash -LiteralPath C:/SwapInput/tests/Swap.Tests.exe -Algorithm SHA256).Hash -eq $manifest.testsSha256)
    [void][IO.Directory]::CreateDirectory('C:/SwapTests')
    Copy-Item -Path C:/SwapInput/* -Destination C:/SwapTests -Recurse -Force
    Check 'Portable publisher metadata' ((Get-Item -LiteralPath C:/SwapTests/Swap.exe).VersionInfo.CompanyName -eq 'KraicDev')
    $policy = (Get-ItemProperty -LiteralPath HKLM:/SOFTWARE/Microsoft/Windows/CurrentVersion/Policies/System -Name EnableLUA).EnableLUA
    @{ enableLUA=$policy; note='UAC behavior is recorded without changing security settings.' } | ConvertTo-Json | Set-Content -LiteralPath C:/SwapResults/uac-environment.json -Encoding UTF8
    Save-GuestStatus 'running' 'Core and real guest read-only backend'
    & C:/SwapTests/tests/Swap.Tests.exe --windows-readonly --graphics-native --resume-registration=C:/SwapInput/Swap.exe --report=C:/SwapResults/core.json *> C:/SwapResults/core.log
    Check 'Core and read-only backend exit' ($LASTEXITCODE -eq 0)
    $core = Get-Content -LiteralPath C:/SwapResults/core.json -Raw | ConvertFrom-Json
    Check 'Core results' ($core.failed -eq 0 -and $core.passed -eq $manifest.expectedCorePassed -and $core.passed -ge 90) ($core.passed.ToString() + ' passed')
    $coreLog=Get-Content -LiteralPath C:/SwapResults/core.log -Raw
    Check 'Real guest inventory is complete' ($coreLog -match 'Inventory: adapters=\d+; admin=True; complete=True\.')
    Save-GuestStatus 'running' 'Published WPF layout and workflows'
    $ui = Start-Process -FilePath C:/SwapTests/Swap.exe -ArgumentList '--ui-audit=C:/SwapResults/ui' -PassThru -Wait
    Check 'Published UI exit' ($ui.ExitCode -eq 0)
    $uiResults = Get-Content -LiteralPath C:/SwapResults/ui/results.json -Raw | ConvertFrom-Json
    Check 'Published UI results' ($uiResults.failed -eq 0 -and $uiResults.passed -eq $manifest.expectedUiPassed) ($uiResults.passed.ToString() + ' passed')
    $displayAfter = @(Get-CimInstance Win32_PnPSignedDriver | Where-Object DeviceClass -eq 'DISPLAY' | Select-Object DeviceID,InfName,DriverVersion)
    $storeAfter = @(Get-WindowsDriver -Online | Where-Object ClassName -eq 'Display' | Select-Object Driver,ProviderName,Version)
    Check 'Guest active display driver unchanged' (($displayBefore | ConvertTo-Json -Depth 4 -Compress) -eq ($displayAfter | ConvertTo-Json -Depth 4 -Compress))
    Check 'Guest display driver store unchanged' (($storeBefore | ConvertTo-Json -Depth 4 -Compress) -eq ($storeAfter | ConvertTo-Json -Depth 4 -Compress))
    $completed = $true
    Save-GuestStatus 'completed' 'Core, real guest inventory, portable runtime and 155 rendered UI cases completed.'
} catch {
    if (Test-Path -LiteralPath C:/SwapResults) { Save-GuestStatus 'failed' $_.Exception.ToString() }
    throw
} finally {
    if (Test-Path -LiteralPath C:/SwapResults) {
        @{ author='KraicDev'; completed=$completed; checks=$results; scope='Real isolated Windows execution. Driver transitions remain simulated; virtual display is not physical AMD/NVIDIA/Intel hardware.' } | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath C:/SwapResults/summary.json -Encoding UTF8
        if ($null -ne $serial -and $serial.IsOpen) {
            Compress-Archive -Path C:/SwapResults/* -DestinationPath C:/SwapGuestResults.zip -CompressionLevel Optimal -Force
            $bytes = [IO.File]::ReadAllBytes('C:/SwapGuestResults.zip'); $encoded = [Convert]::ToBase64String($bytes)
            $serial.WriteLine('SWAP_BEGIN ' + $bytes.Length)
            for ($index=0; $index -lt $encoded.Length; $index+=1536) { $serial.WriteLine('SWAP_DATA ' + $encoded.Substring($index,[Math]::Min(1536,$encoded.Length-$index))) }
            $serial.WriteLine('SWAP_END ' + (Get-FileHash -LiteralPath C:/SwapGuestResults.zip -Algorithm SHA256).Hash)
            $serial.Close(); $serial.Dispose()
        }
    }
}
if ($completed -and $Preview) { Start-Process -FilePath C:/SwapTests/Swap.exe -ArgumentList '--readonly' }
