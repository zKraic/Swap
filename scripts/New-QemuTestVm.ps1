# KraicDev - isolated Windows 10 LTSC evaluation VM, no host restart, drivers or network changes.
param([string]$LabRoot='D:/KraicDev/Swap-VM',
    [ValidatePattern('^windows10-[A-Za-z0-9-]+\.qcow2$')][string]$DiskFileName=('windows10-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.qcow2'))
$ErrorActionPreference = 'Stop'
if (Get-Process -Name qemu-system-x86_64 -ErrorAction SilentlyContinue) { throw 'Close the test guest before rebuilding its input CD.' }
$root = Split-Path $PSScriptRoot -Parent
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$inputPath = Join-Path $LabRoot 'input'
$bundle = Join-Path $inputPath 'bundle'
[void][IO.Directory]::CreateDirectory($bundle)
[void][IO.Directory]::CreateDirectory((Join-Path $bundle 'tests'))
Copy-Item -LiteralPath (Join-Path $root 'dist/Swap.exe') -Destination (Join-Path $bundle 'Swap.exe') -Force
Copy-Item -LiteralPath (Join-Path $root 'artifacts/vm/bundle/tests/Swap.Tests.exe') -Destination (Join-Path $bundle 'tests/Swap.Tests.exe') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-GuestTests.ps1') -Destination (Join-Path $bundle 'Invoke-GuestTests.ps1') -Force
@{ expectedCorePassed=([int](Get-Content (Join-Path $root 'artifacts/test-results.json') -Raw|ConvertFrom-Json).passed+3); expectedUiPassed=126; expectedCaptures=33; author='KraicDev'; hostComputer=$env:COMPUTERNAME; hostUuid=[string](Get-CimInstance Win32_ComputerSystemProduct).UUID;
    appSha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'Swap.exe') -Algorithm SHA256).Hash;
    testsSha256=(Get-FileHash -LiteralPath (Join-Path $bundle 'tests/Swap.Tests.exe') -Algorithm SHA256).Hash } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $bundle 'manifest.json') -Encoding UTF8
# These credentials belong only to the offline disposable guest, never the host or TeamViewer.
$guestPassword = 'Vm!' + [Guid]::NewGuid().ToString('N')
$bootstrap = 'powershell.exe -NoLogo -NoProfile -Command "' +
    '[void][IO.Directory]::CreateDirectory(''C:\SwapResults''); Start-Transcript C:\SwapResults\bootstrap.log; ' +
    '$volume=Get-CimInstance Win32_LogicalDisk | Where-Object VolumeName -eq ''SWAPTEST''; if (@($volume).Count -ne 1) { throw ''Test input volume missing'' }; ' +
    '$inputDrive=$volume.DeviceID+''\bundle''; Copy-Item -LiteralPath $inputDrive -Destination C:\SwapInput -Recurse; ' +
    '& ([scriptblock]::Create([IO.File]::ReadAllText(''C:\SwapInput\Invoke-GuestTests.ps1'')))"'
$escapedCommand = [Security.SecurityElement]::Escape($bootstrap)
$answer = @"
<?xml version="1.0" encoding="utf-8"?>
<unattend xmlns="urn:schemas-microsoft-com:unattend" xmlns:wcm="http://schemas.microsoft.com/WMIConfig/2002/State">
 <settings pass="windowsPE">
  <component name="Microsoft-Windows-International-Core-WinPE" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
   <SetupUILanguage><UILanguage>en-US</UILanguage></SetupUILanguage><InputLocale>en-US</InputLocale><SystemLocale>en-US</SystemLocale><UILanguage>en-US</UILanguage><UserLocale>en-US</UserLocale>
  </component>
  <component name="Microsoft-Windows-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
   <DiskConfiguration><Disk wcm:action="add"><DiskID>0</DiskID><WillWipeDisk>true</WillWipeDisk>
    <CreatePartitions><CreatePartition wcm:action="add"><Order>1</Order><Type>Primary</Type><Size>500</Size></CreatePartition><CreatePartition wcm:action="add"><Order>2</Order><Type>Primary</Type><Extend>true</Extend></CreatePartition></CreatePartitions>
    <ModifyPartitions><ModifyPartition wcm:action="add"><Order>1</Order><PartitionID>1</PartitionID><Active>true</Active><Format>NTFS</Format><Label>System</Label></ModifyPartition><ModifyPartition wcm:action="add"><Order>2</Order><PartitionID>2</PartitionID><Format>NTFS</Format><Label>Windows</Label><Letter>C</Letter></ModifyPartition></ModifyPartitions>
   </Disk><WillShowUI>OnError</WillShowUI></DiskConfiguration>
   <ImageInstall><OSImage><InstallFrom><MetaData wcm:action="add"><Key>/IMAGE/INDEX</Key><Value>1</Value></MetaData></InstallFrom><InstallTo><DiskID>0</DiskID><PartitionID>2</PartitionID></InstallTo><WillShowUI>OnError</WillShowUI></OSImage></ImageInstall>
   <UserData><AcceptEula>true</AcceptEula><FullName>KraicDev</FullName><Organization>KraicDev</Organization></UserData>
   <DynamicUpdate><Enable>false</Enable></DynamicUpdate>
  </component>
 </settings>
 <settings pass="specialize"><component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"><ComputerName>SWAPTESTVM</ComputerName><RegisteredOwner>KraicDev</RegisteredOwner><RegisteredOrganization>KraicDev</RegisteredOrganization><TimeZone>Pacific SA Standard Time</TimeZone></component></settings>
 <settings pass="oobeSystem">
  <component name="Microsoft-Windows-International-Core" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS"><InputLocale>en-US</InputLocale><SystemLocale>en-US</SystemLocale><UILanguage>en-US</UILanguage><UserLocale>en-US</UserLocale></component>
  <component name="Microsoft-Windows-Shell-Setup" processorArchitecture="amd64" publicKeyToken="31bf3856ad364e35" language="neutral" versionScope="nonSxS">
   <OOBE><HideEULAPage>true</HideEULAPage><HideOEMRegistrationScreen>true</HideOEMRegistrationScreen><HideOnlineAccountScreens>true</HideOnlineAccountScreens><HideWirelessSetupInOOBE>true</HideWirelessSetupInOOBE><ProtectYourPC>1</ProtectYourPC></OOBE>
   <UserAccounts><LocalAccounts><LocalAccount wcm:action="add"><Name>KraicDevVM</Name><DisplayName>KraicDev VM</DisplayName><Group>Administrators</Group><Password><Value>$guestPassword</Value><PlainText>true</PlainText></Password></LocalAccount></LocalAccounts></UserAccounts>
   <AutoLogon><Username>KraicDevVM</Username><Password><Value>$guestPassword</Value><PlainText>true</PlainText></Password><Enabled>true</Enabled><LogonCount>2</LogonCount></AutoLogon>
   <FirstLogonCommands><SynchronousCommand wcm:action="add"><Order>1</Order><Description>Run isolated Swap tests</Description><CommandLine>$escapedCommand</CommandLine></SynchronousCommand></FirstLogonCommands>
  </component>
 </settings>
</unattend>
"@
[xml]$answerValidation = $answer
$answer | Set-Content -LiteralPath (Join-Path $inputPath 'autounattend.xml') -Encoding UTF8
# IMAPI creates a data CD; no optical burner or physical disk is touched.
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
public static class SwapIsoWriter {
 public static void Save(string path, object streamObject, int blockSize, int blocks) {
  var source=(IStream)streamObject;
  var buffer=new byte[blockSize];
  using(var target=new FileStream(path,FileMode.Create,FileAccess.Write,FileShare.None)) {
   for(int i=0;i<blocks;i++) { source.Read(buffer,blockSize,IntPtr.Zero); target.Write(buffer,0,blockSize); }
  }
 }
}
'@
$image = New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$image.FileSystemsToCreate = 3
$image.VolumeName = 'SWAPTEST'
$image.Root.AddTree($inputPath,$false)
$imageResult = $image.CreateResultImage()
$payloadIso = Join-Path $LabRoot 'swap-tests.iso'
[SwapIsoWriter]::Save($payloadIso,$imageResult.ImageStream,$imageResult.BlockSize,$imageResult.TotalBlocks)
$qemuImageTool = Join-Path $LabRoot 'tools/qemu/qemu-img.exe'
$disk = Join-Path $LabRoot $DiskFileName
if (Test-Path -LiteralPath $disk) { throw 'The requested virtual disk already exists. Choose a new name; existing guests are preserved.' }
& $qemuImageTool create -f qcow2 $disk 64G
if ($LASTEXITCODE -ne 0) { throw 'Virtual disk creation failed.' }
@{ author='KraicDev'; root=$LabRoot; disk=$disk; payload=$payloadIso; windowsIso=(Join-Path $LabRoot 'downloads/Windows10-LTSC-Eval.iso');
    emulator=(Join-Path $LabRoot 'tools/qemu/qemu-system-x86_64.exe'); serialLog=(Join-Path $LabRoot 'guest-serial.log'); accelerator='tcg'; guestName='SWAPTESTVM'; hostRestart=$false; network=$false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts/vm/qemu-run.json') -Encoding UTF8
Write-Output ('QEMU isolated input and virtual disk prepared: ' + $LabRoot)

