# KraicDev. Replace only the private guest's read-only test CD, without restarting the host or guest.
param([string]$ApplicationPath='dist/Swap.exe')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw | ConvertFrom-Json
$launch=Get-Content (Join-Path $root 'artifacts/vm/qemu-process.json') -Raw | ConvertFrom-Json
if ((Get-Process -Id $launch.pid -ErrorAction Stop).Path -ne $config.emulator) { throw 'Unexpected emulator process.' }
$lab=[IO.Path]::GetFullPath($config.root).TrimEnd('\')+'\'
if($lab-ne'D:\KraicDev\Swap-VM\'){throw 'Only the dedicated VM lab is accepted.'}
$name='payload-'+(Get-Date -Format 'yyyyMMdd-HHmmss')
$input=Join-Path $lab $name
$bundle=Join-Path $input 'bundle'
[void][IO.Directory]::CreateDirectory((Join-Path $bundle 'tests'))
Copy-Item (Join-Path $root $ApplicationPath) (Join-Path $bundle 'Swap.exe')
Copy-Item (Join-Path $root 'artifacts/vm/bundle/tests/Swap.Tests.exe') (Join-Path $bundle 'tests/Swap.Tests.exe')
Copy-Item (Join-Path $PSScriptRoot 'Invoke-GuestTests.ps1') (Join-Path $bundle 'Invoke-GuestTests.ps1')
@{expectedCorePassed=([int](Get-Content (Join-Path $root 'artifacts/test-results.json') -Raw|ConvertFrom-Json).passed+3); expectedUiPassed=127; expectedCaptures=33;author='KraicDev';hostComputer=$env:COMPUTERNAME;hostUuid=[string](Get-CimInstance Win32_ComputerSystemProduct).UUID;
appSha256=(Get-FileHash (Join-Path $bundle 'Swap.exe') -Algorithm SHA256).Hash;testsSha256=(Get-FileHash (Join-Path $bundle 'tests/Swap.Tests.exe') -Algorithm SHA256).Hash}|ConvertTo-Json|Set-Content (Join-Path $bundle 'manifest.json') -Encoding UTF8
Add-Type -TypeDefinition @'
using System; using System.IO; using System.Runtime.InteropServices; using System.Runtime.InteropServices.ComTypes;
public static class KraicDevPayloadIso {
 public static void Save(string path,object streamObject,int blockSize,int blocks){
  var source=(IStream)streamObject; var buffer=new byte[blockSize]; var read=Marshal.AllocHGlobal(sizeof(int));
  try{using(var target=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
   for(int i=0;i<blocks;i++){source.Read(buffer,blockSize,read);if(Marshal.ReadInt32(read)!=blockSize)throw new IOException("Incomplete ISO block");target.Write(buffer,0,blockSize);}
  }}finally{Marshal.FreeHGlobal(read);}
 }
}
'@
$image=New-Object -ComObject IMAPI2FS.MsftFileSystemImage
$image.FileSystemsToCreate=3; $image.VolumeName='SWAPTEST'; $image.Root.AddTree($input,$false)
$result=$image.CreateResultImage(); $iso=Join-Path $lab ($name+'.iso')
[KraicDevPayloadIso]::Save($iso,$result.ImageStream,$result.BlockSize,$result.TotalBlocks)
$client=[Net.Sockets.TcpClient]::new('127.0.0.1',44442)
try{
 $stream=$client.GetStream();$stream.ReadTimeout=5000;$stream.WriteTimeout=5000
 $reader=[IO.StreamReader]::new($stream);$writer=[IO.StreamWriter]::new($stream);$writer.AutoFlush=$true
 if(!($reader.ReadLine()|ConvertFrom-Json).QMP){throw 'No QMP greeting'}
 function Request([string]$command,[hashtable]$arguments=@{}){
  $id=[Guid]::NewGuid().ToString('N');$writer.WriteLine((@{execute=$command;arguments=$arguments;id=$id}|ConvertTo-Json -Depth 8 -Compress))
  do{$line=$reader.ReadLine();if(!$line){throw 'VM disconnected'};$response=$line|ConvertFrom-Json}until($response.id-eq$id)
  if($response.error){throw $response.error.desc};return $response.return
 }
 $null=Request 'qmp_capabilities'
 if((Request 'query-name').name-ne'Swap - KraicDev test VM'){throw 'Unexpected VM identity'}
 $blocks=@(Request 'query-block')
 $drive=@($blocks|Where-Object {$_.inserted.file -and [IO.Path]::GetFullPath($_.inserted.file) -eq [IO.Path]::GetFullPath($config.payload)})
 if($drive.Count-ne1){throw 'Expected exactly one current payload CD'}
 $null=Request 'blockdev-change-medium' @{device=$drive[0].device;filename=$iso;format='raw';'read-only-mode'='read-only';force=$true}
 $config.payload=$iso;$config|ConvertTo-Json|Set-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Encoding UTF8
 Write-Output ('KraicDev: read-only guest test CD updated: '+$iso)
}finally{$client.Dispose()}
