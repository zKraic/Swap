# Encode the brand mark as a Windows icon at all common shell sizes.
# Sizes below 256 px are classic 32-bit DIBs with an AND mask: the taskbar, Alt+Tab and older shell paths draw a
# PNG-compressed small frame as an empty icon. Only the 256 px frame is PNG, as Windows expects.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'assets/swap-logo.png'))
$images = @()
try {
 foreach ($size in @(16,20,24,32,40,48,64,128,256)) {
  $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
  $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
  $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
  $graphics.DrawImage($source, 0, 0, $size, $size)
  $graphics.Dispose()
  if ($size -eq 256) {
   $stream = [IO.MemoryStream]::new()
   $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
   $images += @{ Size = $size; Bytes = $stream.ToArray() }
   $stream.Dispose()
  } else {
   $rect = [System.Drawing.Rectangle]::new(0, 0, $size, $size)
   $data = $bitmap.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
   $pixels = [byte[]]::new($size * $size * 4)
   for ($y = 0; $y -lt $size; $y++) { [Runtime.InteropServices.Marshal]::Copy([IntPtr]::Add($data.Scan0, $y * $data.Stride), $pixels, $y * $size * 4, $size * 4) }
   $bitmap.UnlockBits($data)
   $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
   $stream = [IO.MemoryStream]::new(); $writer = [IO.BinaryWriter]::new($stream)
   # BITMAPINFOHEADER: the height covers the colour rows and the AND mask.
   $writer.Write([uint32]40); $writer.Write([int32]$size); $writer.Write([int32]($size * 2)); $writer.Write([uint16]1); $writer.Write([uint16]32)
   $writer.Write([uint32]0); $writer.Write([uint32]($size * $size * 4 + $maskRow * $size)); $writer.Write([int32]0); $writer.Write([int32]0); $writer.Write([uint32]0); $writer.Write([uint32]0)
   for ($y = $size - 1; $y -ge 0; $y--) { $writer.Write($pixels, $y * $size * 4, $size * 4) }
   for ($y = $size - 1; $y -ge 0; $y--) {
    $row = [byte[]]::new($maskRow)
    for ($x = 0; $x -lt $size; $x++) { if ($pixels[($y * $size + $x) * 4 + 3] -eq 0) { $row[[int][Math]::Floor($x / 8)] = $row[[int][Math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) } }
    $writer.Write($row)
   }
   $writer.Flush(); $images += @{ Size = $size; Bytes = $stream.ToArray() }; $writer.Dispose(); $stream.Dispose()
  }
  $bitmap.Dispose()
 }
 $output = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $root 'assets/swap.ico')))
 try {
  $output.Write([uint16]0); $output.Write([uint16]1); $output.Write([uint16]$images.Count)
  $offset = 6 + 16 * $images.Count
  foreach ($image in $images) {
   $dimension = if ($image.Size -eq 256) { 0 } else { $image.Size }
   $output.Write([byte]$dimension); $output.Write([byte]$dimension); $output.Write([byte]0); $output.Write([byte]0)
   $output.Write([uint16]1); $output.Write([uint16]32); $output.Write([uint32]$image.Bytes.Length); $output.Write([uint32]$offset)
   $offset += $image.Bytes.Length
  }
  foreach ($image in $images) { $output.Write([byte[]]$image.Bytes) }
 } finally { $output.Dispose() }
} finally { $source.Dispose() }
