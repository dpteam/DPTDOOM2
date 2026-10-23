# Rebuilds resources/dptdoom.ico as a Windows-XP-compatible icon.
#
#   powershell -NoProfile -File tools/fix-icon.ps1 [-Source resources/dptdoom.ico] [-Out resources/dptdoom.ico]
#
# Why this exists: the icon shipped with the port had every image stored as a
# PNG stream inside the .ico. Windows Vista and later accept that, but Windows
# XP does not, so the game would show a blank or garbage icon on the very
# platform the port targets. System.Drawing also misreads PNG entries, which
# made the icon look like random pixels.
#
# This script decodes each image and rewrites it as a classic DIB entry:
# a BITMAPINFOHEADER, 32bpp BGRA rows bottom-up, and a 1bpp AND mask.
# Transparency then comes from the alpha channel, which is what XP expects.

param(
    [string]$Source = "resources/dptdoom.ico",
    [string]$Out = "resources/dptdoom.ico"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

$sourcePath = (Resolve-Path $Source).Path
$bytes = [IO.File]::ReadAllBytes($sourcePath)

if ([BitConverter]::ToUInt16($bytes, 2) -ne 1) {
    throw "Not an icon file: $Source"
}

$count = [BitConverter]::ToUInt16($bytes, 4)

# Sizes that are worth keeping. 256 is deliberately omitted: as a DIB it is
# ~270 KB and no supported Windows version needs it for a game window.
$wanted = @(16, 24, 32, 48, 64, 128)

$images = @()

for ($i = 0; $i -lt $count; $i++) {
    $entry = 6 + $i * 16
    $width = $bytes[$entry]
    $height = $bytes[$entry + 1]
    if ($width -eq 0) { $width = 256 }
    if ($height -eq 0) { $height = 256 }

    $size = [BitConverter]::ToInt32($bytes, $entry + 8)
    $offset = [BitConverter]::ToInt32($bytes, $entry + 12)

    if ($wanted -notcontains $width) { continue }

    $isPng = $size -gt 8 -and $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50
    if (-not $isPng) {
        Write-Host "  $width x $height already DIB, skipping"
        continue
    }

    $png = New-Object byte[] $size
    [Array]::Copy($bytes, $offset, $png, 0, $size)

    $stream = New-Object IO.MemoryStream(, $png)
    $bitmap = New-Object System.Drawing.Bitmap($stream)

    if ($bitmap.Width -ne $width -or $bitmap.Height -ne $height) {
        throw "Entry $i says ${width}x${height} but the PNG is $($bitmap.Width)x$($bitmap.Height)"
    }

    $images += @{ Width = $width; Height = $height; Bitmap = $bitmap }
    Write-Host "  decoded $width x $height"
}

if ($images.Count -eq 0) {
    throw "No usable images found in $Source"
}

$images = $images | Sort-Object { $_.Width }

# Build the DIB payload for one image.
#
# Bytes are written into a plain array by index rather than through a
# BinaryWriter over a MemoryStream: the stream overloads in PowerShell are easy
# to get subtly wrong, and a wrong header produces an icon that looks like
# noise instead of failing loudly.
function New-Dib([System.Drawing.Bitmap]$bitmap) {
    $w = $bitmap.Width
    $h = $bitmap.Height

    $xorStride = $w * 4
    $andStride = [int]([Math]::Floor(($w + 31) / 32) * 4)
    $xorSize = $xorStride * $h
    $andSize = $andStride * $h

    $data = New-Object byte[] (40 + $xorSize + $andSize)

    # BITMAPINFOHEADER. Height is doubled: XOR image plus AND mask.
    [Array]::Copy([BitConverter]::GetBytes([int]40), 0, $data, 0, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]$w), 0, $data, 4, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]($h * 2)), 0, $data, 8, 4)
    [Array]::Copy([BitConverter]::GetBytes([int16]1), 0, $data, 12, 2)
    [Array]::Copy([BitConverter]::GetBytes([int16]32), 0, $data, 14, 2)
    [Array]::Copy([BitConverter]::GetBytes([int]0), 0, $data, 16, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]($xorSize + $andSize)), 0, $data, 20, 4)
    # The four remaining fields (resolution and palette counts) stay zero.

    # XOR rows, bottom-up, BGRA.
    $pos = 40
    for ($y = $h - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $w; $x++) {
            $c = $bitmap.GetPixel($x, $y)
            $data[$pos] = $c.B
            $data[$pos + 1] = $c.G
            $data[$pos + 2] = $c.R
            $data[$pos + 3] = $c.A
            $pos += 4
        }
    }

    # AND mask stays zero: transparency comes from the alpha channel. The
    # stride must be 4-byte aligned, which $andSize already accounts for.
    return $data
}

$payloads = @()
foreach ($image in $images) {
    $dib = New-Dib $image.Bitmap
    $payloads += @{ Width = $image.Width; Height = $image.Height; Data = $dib }
    Write-Host "  encoded $($image.Width) x $($image.Height) as DIB ($($dib.Length) bytes)"
    $image.Bitmap.Dispose()
}

# Assemble the .ico.
$total = 6 + $payloads.Count * 16
foreach ($p in $payloads) { $total += $p.Data.Length }

$output = New-Object byte[] $total
$pos = 0

# ICONDIR
[Array]::Copy([BitConverter]::GetBytes([int16]0), 0, $output, 0, 2)
[Array]::Copy([BitConverter]::GetBytes([int16]1), 0, $output, 2, 2)
[Array]::Copy([BitConverter]::GetBytes([int16]$payloads.Count), 0, $output, 4, 2)
$pos = 6

# ICONDIRENTRY for each image
$dataOffset = 6 + $payloads.Count * 16
foreach ($p in $payloads) {
    $output[$pos] = if ($p.Width -ge 256) { 0 } else { $p.Width }
    $output[$pos + 1] = if ($p.Height -ge 256) { 0 } else { $p.Height }
    $output[$pos + 2] = 0   # palette size
    $output[$pos + 3] = 0   # reserved
    [Array]::Copy([BitConverter]::GetBytes([int16]1), 0, $output, $pos + 4, 2)   # planes
    [Array]::Copy([BitConverter]::GetBytes([int16]32), 0, $output, $pos + 6, 2)  # bits per pixel
    [Array]::Copy([BitConverter]::GetBytes([int]$p.Data.Length), 0, $output, $pos + 8, 4)
    [Array]::Copy([BitConverter]::GetBytes([int]$dataOffset), 0, $output, $pos + 12, 4)

    $pos += 16
    $dataOffset += $p.Data.Length
}

# Image payloads
foreach ($p in $payloads) {
    [Array]::Copy($p.Data, 0, $output, $pos, $p.Data.Length)
    $pos += $p.Data.Length
}

[IO.File]::WriteAllBytes((Join-Path (Get-Location) $Out), $output)

Write-Host ""
Write-Host "Wrote $Out ($($output.Length) bytes, $($payloads.Count) DIB images)"
