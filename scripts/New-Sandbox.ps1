param([Parameter(Mandatory)][string]$PortableFolder)
$ErrorActionPreference = 'Stop'
$resolved = (Resolve-Path -LiteralPath $PortableFolder).Path
if (-not (Test-Path -LiteralPath (Join-Path $resolved 'Swap.exe'))) { throw 'La carpeta debe contener Swap.exe.' }
$escaped = [System.Security.SecurityElement]::Escape($resolved)
$config = @"
<Configuration>
 <VGpu>Disable</VGpu>
 <Networking>Disable</Networking>
 <AudioInput>Disable</AudioInput>
 <VideoInput>Disable</VideoInput>
 <PrinterRedirection>Disable</PrinterRedirection>
 <ClipboardRedirection>Disable</ClipboardRedirection>
 <MappedFolders><MappedFolder><HostFolder>$escaped</HostFolder><SandboxFolder>C:\SwapInput</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder></MappedFolders>
 <LogonCommand><Command>powershell.exe -NoProfile -Command "Copy-Item -LiteralPath C:\SwapInput -Destination C:\Swap -Recurse; Start-Process C:\Swap\Swap.exe -ArgumentList --demo"</Command></LogonCommand>
</Configuration>
"@
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'artifacts/Swap-Demo.wsb'
New-Item -ItemType Directory -Path (Split-Path $out -Parent) -Force | Out-Null
$config | Set-Content -LiteralPath $out -Encoding utf8
Write-Output ('Configuración creada: ' + $out)
Write-Output 'No activa características de Windows, no reinicia y no inicia Sandbox automáticamente.'
