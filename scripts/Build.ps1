param([ValidateSet('win-x64','win-x86','win-arm64')][string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $root 'artifacts/tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $root
try {
 & $dotnet run --project tests/Swap.Tests -c Release -- --report=artifacts/test-results.json
 if ($LASTEXITCODE -ne 0) { throw 'Pruebas fallidas: no se publicará.' }
 $output = Join-Path $root ('dist/releases/Swap-' + $Runtime + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
 & $dotnet publish src/Swap.App/Swap.App.csproj -c Release -r $Runtime --self-contained true -o $output -p:DebugType=None -p:DebugSymbols=false -p:EnableCompressionInSingleFile=true
 if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }
 $extra = @(Get-ChildItem -LiteralPath $output -File | Where-Object Name -ne 'Swap.exe')
 if ($extra.Count -ne 0) { throw ('El publish no es un EXE único: ' + ($extra.Name -join ', ')) }
 $name = if ($Runtime -eq 'win-x64') { 'Swap.exe' } else { 'Swap-' + $Runtime + '.exe' }
 $exe = Join-Path $root ('dist/' + $name)
 try { Copy-Item -LiteralPath (Join-Path $output 'Swap.exe') -Destination $exe -Force }
 catch [System.IO.IOException] {
  # A running portable copy must not be terminated to publish a new release.
  $version=[Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output 'Swap.exe')).ProductVersion.Split('+')[0]
  $name='Swap-'+$version+'-'+$Runtime+'.exe'; $exe=Join-Path $root ('dist/'+$name)
  if(Test-Path -LiteralPath $exe){throw 'Versioned delivery already exists. Close it or use the immutable release folder.'}
  Copy-Item -LiteralPath (Join-Path $output 'Swap.exe') -Destination $exe
  Write-Warning ('The current copy is open. A separate portable delivery was created: '+$name)
 }
 $hash = Get-FileHash -LiteralPath $exe -Algorithm SHA256
 ($hash.Hash + '  ' + $name) | Set-Content -LiteralPath ($exe + '.sha256') -Encoding utf8
 Write-Output ('EXE portable: ' + $exe)
 Write-Output ('SHA-256: ' + $hash.Hash)
} finally { Pop-Location }
