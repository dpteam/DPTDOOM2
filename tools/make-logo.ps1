# Prepares the project logo for the README.
#
#   powershell -NoProfile -File tools/make-logo.ps1
#
# The source art lives in refs/ (never committed) and is 65x34. This copies it into
# docs/images at a whole-number scale.
#
# Nearest-neighbour on purpose: the logo is pixel art, and every other resampling
# filter would blend the pixels into a soft blur - which is exactly what it must not
# look like. Scaling by an integer also keeps every source pixel an exact square, so
# the result is the same drawing, just bigger, with nothing broken or invented.

param(
    [string]$Source = "refs\doom-bigupper-dpt-doom.png",
    [string]$Out = "docs\images\logo.png",
    [int]$Scale = 8
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Source)) {
    throw "Logo source not found: $Source. It lives in refs/ and is not part of the repository."
}

# Deliberately not called $source: a typed [string] parameter keeps its type, so
# assigning an Image to it would be coerced back to a string and Dispose would fail
# with "method not found" on a String.
$artwork = [System.Drawing.Image]::FromFile((Resolve-Path $Source))

try {
    $width = $artwork.Width * $Scale
    $height = $artwork.Height * $Scale

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)

    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
        $graphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy

        $graphics.DrawImage($artwork, 0, 0, $width, $height)
    }
    finally {
        $graphics.Dispose()
    }

    $directory = Split-Path -Parent $Out
    if ($directory -and -not (Test-Path $directory)) {
        New-Item -ItemType Directory -Force $directory | Out-Null
    }

    $bitmap.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    Write-Host "Saved $Out ($($artwork.Width)x$($artwork.Height) at ${Scale}x = ${width}x${height})"
}
finally {
    $artwork.Dispose()
}
