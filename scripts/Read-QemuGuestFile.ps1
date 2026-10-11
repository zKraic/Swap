# KraicDev. Read only bounded diagnostic files from the private guest.
param([ValidateSet('Status','UiResults','WindowFit','CoreLog','Diagnostics')][string]$Report='Status')
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$config=Get-Content (Join-Path $root 'artifacts/vm/qemu-run.json') -Raw|ConvertFrom-Json
$launch=Get-Content (Join-Path $root 'artifacts/vm/qemu-process.json') -Raw|ConvertFrom-Json
if((Get-Process -Id $launch.pid -ErrorAction Stop).Path-ne$config.emulator){throw 'Unexpected emulator process'}
$path=@{Status='C:\SwapResults\status.json';UiResults='C:\SwapResults\ui\results.json';WindowFit='C:\SwapResults\ui\window-fit.json';CoreLog='C:\SwapResults\core.log';Diagnostics='C:\SwapResults\diagnostics.json'}[$Report]
$client=[Net.Sockets.TcpClient]::new('127.0.0.1',44443)
try{
 $stream=$client.GetStream();$stream.ReadTimeout=15000;$reader=[IO.StreamReader]::new($stream);$writer=[IO.StreamWriter]::new($stream);$writer.AutoFlush=$true
 function Request([string]$name,[hashtable]$arguments){
  $id=[Guid]::NewGuid().ToString('N');$writer.WriteLine((@{execute=$name;arguments=$arguments;id=$id}|ConvertTo-Json -Depth 8 -Compress))
  do{$line=$reader.ReadLine();if(!$line){throw 'Guest disconnected'};$response=$line.TrimStart([char]0xFFFD,[char]255)|ConvertFrom-Json}until($response.id-eq$id)
  if($response.error){throw $response.error.desc};$response.return
 }
 $stream.WriteByte(255);$null=Request 'guest-sync-delimited' @{id=[long](Get-Date).Ticks}
 $handle=Request 'guest-file-open' @{path=$path;mode='r'}
 try{
  $data=Request 'guest-file-read' @{handle=$handle;count=262144}
  if(!$data.eof-and$data.count-eq262144){throw 'Diagnostic file exceeds allowed output bounds'}
  $bytes=[Convert]::FromBase64String($data.'buf-b64')
  $textReader=[IO.StreamReader]::new([IO.MemoryStream]::new($bytes),[Text.Encoding]::UTF8,$true)
  try{$textReader.ReadToEnd()}finally{$textReader.Dispose()}
 }finally{$null=Request 'guest-file-close' @{handle=$handle}}
}finally{$client.Dispose()}
