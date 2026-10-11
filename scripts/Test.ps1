param([switch]$ReadOnlyInventory, [switch]$ReadOnlyCatalog)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $root 'artifacts/tools/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $root
try {
 $arguments = @('run','--project','tests/Swap.Tests','-c','Release','--','--report=artifacts/test-results.json')
 if ($ReadOnlyInventory) { $arguments += '--windows-readonly' }
 if ($ReadOnlyCatalog) { $arguments += '--windows-update-readonly' }
 & $dotnet @arguments
 if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron.' }
 & $dotnet build src/Swap.App/Swap.App.csproj -c Release
 if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }
} finally { Pop-Location }
