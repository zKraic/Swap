param([string]$Output = 'dist/Swap-source-v0.4.0.zip')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    # Only files selected by Git: never sweep the workspace or collect Data/tool downloads.
    $files = @(& git ls-files --cached --others --exclude-standard | Sort-Object -Unique)
    if ($LASTEXITCODE -ne 0 -or $files.Count -eq 0) { throw 'Prepara primero los archivos fuente con git add .' }
    $destination = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }))
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($destination,[IO.FileMode]::CreateNew)
    $zip = [IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
    try {
        foreach ($file in $files) {
            if ($file -match '(^|/)(Data|artifacts|dist|bin|obj)/') { throw ('Archivo excluido detectado: ' + $file) }
            $path = Join-Path $root $file
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { continue } # A staged file may have been removed in the working tree.
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$path,$file.Replace('\','/'),[IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose(); $stream.Dispose() }
    Write-Output ('Fuente: ' + $destination)
} finally { Pop-Location }
