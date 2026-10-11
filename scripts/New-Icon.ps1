# Encode the generated brand mark as a Windows icon at all common shell sizes.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'assets/swap-logo.png'))
$images = @()
try {
 foreach ($size in @(16,24,32,48,64,128,256)) {
  $bitmap = [System.Drawing.Bitmap]::new($size,$size)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.DrawImage($source,0,0,$size,$size)
  $stream = [IO.MemoryStream]::new()
  $bitmap.Save($stream,[System.Drawing.Imaging.ImageFormat]::Png)
  $images += @{ Size=$size; Bytes=$stream.ToArray() }
  $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose()
 }
 $writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $root 'assets/swap.ico')))
 try {
  $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
  $offset = 6 + 16 * $images.Count
  foreach ($image in $images) {
   $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
   $writer.Write([byte]$dimension); $writer.Write([byte]$dimension); $writer.Write([byte]0); $writer.Write([byte]0)
   $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$image.Bytes.Length); $writer.Write([uint32]$offset)
   $offset += $image.Bytes.Length
  }
  foreach ($image in $images) { $writer.Write([byte[]]$image.Bytes) }
 } finally { $writer.Dispose() }
} finally { $source.Dispose() }
