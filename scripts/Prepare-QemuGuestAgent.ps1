# KraicDev. Optional lab recovery path when FirstLogonCommands did not start the tests.
# Copies only the dedicated VM image and prepares a guest service, never a host service.
param()
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
if (Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue) { throw 'Shut down the guest normally before preparing an offline copy.' }
$lab=[IO.Path]::GetFullPath($config.root).TrimEnd('\')+'\'
if ($lab -ne 'D:\KraicDev\Swap-VM\' -or ![IO.Path]::GetFullPath($config.disk).StartsWith($lab,[StringComparison]::OrdinalIgnoreCase)) { throw 'Only the dedicated disposable lab is accepted.' }
$newDisk=Join-Path $lab ('windows10-agent-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.vhd')
if (Test-Path -LiteralPath $newDisk) { throw 'The new disk already exists.' }
$qemuImg=Join-Path (Split-Path $config.emulator -Parent) 'qemu-img.exe'
$qemuDirectory=Split-Path $config.emulator -Parent
if (!(Test-Path (Join-Path $qemuDirectory 'gspawn-win64-helper.exe'))) {
    if ((Get-Item (Join-Path $qemuDirectory 'libglib-2.0-0.dll')).VersionInfo.FileVersion -ne '2.88.3') { throw 'This pinned helper is for QEMU with GLib 2.88.3 only.' }
    $package=Join-Path $lab 'downloads/glib2-2.88.3.pkg.tar.zst'
    Invoke-WebRequest -Uri 'https://repo.msys2.org/mingw/mingw64/mingw-w64-x86_64-glib2-2.88.3-1-any.pkg.tar.zst' -OutFile $package
    if ((Get-FileHash $package -Algorithm SHA256).Hash -ne '5AA850934D0216F5B7237BD54C3EC660EBFC3A42FD992BC7ABBD611BD9CC7663') { throw 'GLib package changed from the tested lab revision.' }
    $sevenZip=Join-Path $lab 'tools/7zip/7z.exe'
    $unpacked=Join-Path $lab 'tools/glib2'
    & $sevenZip x $package ('-o'+$unpacked) -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GLib package decompression failed.' }
    & $sevenZip x (Join-Path $unpacked 'glib2-2.88.3.pkg.tar') ('-o'+$unpacked+'/extracted') 'mingw64/bin/gspawn-win64-helper*.exe' -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'GLib helper extraction failed.' }
    Copy-Item (Join-Path $unpacked 'extracted/mingw64/bin/gspawn-win64-helper*.exe') $qemuDirectory -Force
}
$inputFormat=if ([IO.Path]::GetExtension($config.disk) -eq '.vhd') { 'vpc' } else { 'qcow2' }
if ($inputFormat -eq 'vpc') {
    # Preserve the existing VHD byte for byte; re-encoding a VPC image is unnecessary.
    Copy-Item -LiteralPath $config.disk -Destination $newDisk
    if ((Get-Item $config.disk).Length -ne (Get-Item $newDisk).Length) { throw 'Incomplete private virtual disk copy.' }
} else {
    & $qemuImg convert -f $inputFormat -O vpc -o subformat=dynamic $config.disk $newDisk
    if ($LASTEXITCODE -ne 0) { throw 'Virtual disk conversion failed.' }
}
$yarp=Join-Path $root 'artifacts/tools/yarp'
$revision='125729d78cc1a72cda47e8e5d8ead260ee46a46c'
if (!(Test-Path -LiteralPath $yarp)) {
    git clone https://github.com/msuhanov/yarp.git $yarp
    if ($LASTEXITCODE -ne 0) { throw 'Registry transaction recovery dependency unavailable.' }
    git -C $yarp checkout --detach $revision
    if ($LASTEXITCODE -ne 0) { throw 'Registry recovery dependency checkout failed.' }
}
if ((git -C $yarp rev-parse HEAD) -ne $revision) { throw 'Unexpected registry recovery dependency revision.' }
$dotnet=Join-Path $root 'artifacts/tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet='dotnet' }
& $dotnet build (Join-Path $PSScriptRoot 'vm/GuestDiskTool') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Guest disk tool build failed.' }
$tool=Join-Path $PSScriptRoot 'vm/GuestDiskTool/bin/Release/net10.0-windows/GuestDiskTool.dll'
Copy-Item (Join-Path $PSScriptRoot 'Invoke-GuestTests.ps1') (Join-Path $lab 'input/bundle/Invoke-GuestTests.ps1') -Force
& $dotnet $tool $newDisk extract-hive
if ($LASTEXITCODE -ne 0) { throw 'Guest hive export failed.' }
python (Join-Path $PSScriptRoot 'vm/RecoverGuestHive.py') ($newDisk+'.hives')
if ($LASTEXITCODE -ne 0) { throw 'Guest registry transaction recovery failed.' }
& $dotnet $tool $newDisk prepare (Join-Path $lab 'input/bundle') (Split-Path $config.emulator -Parent)
if ($LASTEXITCODE -ne 0) { throw 'Guest agent preparation failed.' }
$config.disk=$newDisk
$config | Add-Member -NotePropertyName guestAgent -NotePropertyValue $true -Force
$config | ConvertTo-Json | Set-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Encoding UTF8
Write-Output ('Prepared private guest copy: '+$newDisk)
