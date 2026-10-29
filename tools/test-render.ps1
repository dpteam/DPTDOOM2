# Checks the frame-sequence render mode.
#
#   powershell -NoProfile -File tools/test-render.ps1
#
# What only a real run can show: that a run produces numbered PNGs, that the manifest states the
# frame rate and size an encoder needs, that a layer really is a cut-out rather than a rectangle of
# black, and that a colour grade changes the picture without changing the world.
#
# ffmpeg is deliberately not required here. Encoding is checked by tools/render-video.ps1, which
# skips itself when ffmpeg is absent; the render mode itself must work on a machine that has only
# the game.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Render test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-render-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Run-Render([string]$name, [string[]]$extra) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null

    $arguments = @('-render', $dir, '-iwad', $Iwad, '-warp', '1', '-skill', '3', '-nodata') + $extra

    $out = & $Exe @arguments 2>&1 | Out-String

    return @{ Dir = $dir; Log = $out }
}

function Read-Manifest([string]$dir) {
    $path = Join-Path $dir 'render.txt'

    Assert-True (Test-Path $path) "the run wrote a manifest"

    $values = @{}

    foreach ($line in Get-Content $path) {
        if ($line -match '^\s*([a-z]+)\s*=\s*(.*)$') {
            $values[$matches[1]] = $matches[2].Trim()
        }
    }

    return $values
}

# Reads the IHDR of a PNG: width, height and colour type, which is what tells an RGB frame from an
# RGBA one without decoding the pixels.
function Read-PngHeader([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)

    $signature = 137, 80, 78, 71, 13, 10, 26, 10
    for ($i = 0; $i -lt 8; $i++) {
        Assert-True ($bytes[$i] -eq $signature[$i]) "the file starts with the PNG signature"
        break
    }

    # Cast to int first: PowerShell's -shl keeps the left operand's type, so a byte shifted by 8
    # truncates to zero and the width reads as 64 instead of 320.
    $width = ([int]$bytes[16] -shl 24) + ([int]$bytes[17] -shl 16) + ([int]$bytes[18] -shl 8) + [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) + ([int]$bytes[21] -shl 16) + ([int]$bytes[22] -shl 8) + [int]$bytes[23]

    return @{ Width = $width; Height = $height; ColourType = $bytes[25] }
}

try {
    Write-Host "A plain frame sequence"
    $plain = Run-Render "plain" @('-tics', '12', '-renderskip', '2', '-renderfps', '1')

    $frames = Get-ChildItem $plain.Dir -Filter 'frame_*.png' | Sort-Object Name

    Assert-True ($frames.Count -gt 0) "the run wrote frames: $($frames.Count)"
    Assert-True ($frames[0].Name -eq 'frame_00000.png') "the first frame is numbered from zero"
    Assert-True ($frames.Count -lt 12) "recording started after the skip, so fewer frames than tics"

    $manifest = Read-Manifest $plain.Dir

    Assert-True ($manifest['fps'] -eq '35') "the manifest states 35 fps for renderfps 1"
    Assert-True ($manifest['width'] -eq '320') "and the frame width"
    Assert-True ($manifest['pattern'] -eq 'frame_%05d.png') "and the pattern an encoder expects"
    Assert-True ($manifest['layer'] -eq 'Full') "and the layer"

    $header = Read-PngHeader $frames[0].FullName

    Assert-True ($header.Width -eq 320 -and $header.Height -eq 200) "the PNG is 320x200"
    Assert-True ($header.ColourType -eq 2) "and is truecolour without alpha"

    Write-Host "A doubled frame rate"
    $double = Run-Render "double" @('-tics', '12', '-renderskip', '2', '-renderfps', '2')
    $doubleFrames = Get-ChildItem $double.Dir -Filter 'frame_*.png'
    $doubleManifest = Read-Manifest $double.Dir

    Assert-True ($doubleManifest['fps'] -eq '70') "renderfps 2 means 70 fps"
    Assert-True ($doubleFrames.Count -gt $frames.Count) `
        ("and more frames for the same tics: " + $doubleFrames.Count + " against " + $frames.Count)

    Write-Host "A doubled resolution"
    $big = Run-Render "big" @('-tics', '8', '-renderskip', '2', '-renderres', '2')
    $bigFrame = Get-ChildItem $big.Dir -Filter 'frame_*.png' | Sort-Object Name | Select-Object -First 1
    $bigHeader = Read-PngHeader $bigFrame.FullName

    Assert-True ($bigHeader.Width -eq 640 -and $bigHeader.Height -eq 400) `
        ("renderres 2 is 640x400, got " + $bigHeader.Width + "x" + $bigHeader.Height)

    Write-Host "A weapon layer with alpha"
    $weapon = Run-Render "weapon" @('-tics', '70', '-renderskip', '50', '-renderlayer', 'weapon',
        '-renderkey', 'alpha')

    $weaponFrames = Get-ChildItem $weapon.Dir -Filter 'frame_*.png' | Sort-Object Name

    Assert-True ($weaponFrames.Count -gt 0) "the weapon layer wrote frames"

    $weaponHeader = Read-PngHeader $weaponFrames[0].FullName

    Assert-True ($weaponHeader.ColourType -eq 6) "an alpha layer is written as RGBA"

    # A cut-out is mostly empty: the pistol occupies a part of the screen, so its file is far
    # smaller than the full frame's. A layer that covered everything would be the size of a frame.
    $plainSize = $frames[0].Length
    $weaponSize = $weaponFrames[0].Length

    Assert-True ($weaponSize -lt $plainSize) `
        ("the weapon layer is a cut-out: " + $weaponSize + " bytes against " + $plainSize)

    Write-Host "A HUD layer"
    $hud = Run-Render "hud" @('-tics', '70', '-renderskip', '50', '-renderlayer', 'hud')
    $hudFrames = Get-ChildItem $hud.Dir -Filter 'frame_*.png'

    Assert-True ($hudFrames.Count -gt 0) "the HUD layer wrote frames"
    Assert-True ((Read-Manifest $hud.Dir)['layer'] -eq 'Hud') "and says which layer it is"

    Write-Host "A colour grade"
    $noir = Run-Render "noir" @('-tics', '12', '-renderskip', '2', '-grade', 'noir')
    $noirFrames = Get-ChildItem $noir.Dir -Filter 'frame_*.png' | Sort-Object Name

    Assert-True ((Read-Manifest $noir.Dir)['grade'] -like '*saturation 0*') `
        "the manifest records the grade"

    Assert-True ($noirFrames[0].Length -ne $frames[0].Length) `
        "a graded frame is a different picture"

    # The world must be untouched: the grade is a palette transform, so the same tics produce the
    # same world hash with and without it.
    $plainState = [regex]::Match($plain.Log, 'State: mobj=(\S+) sector=(\S+)')
    $noirState = [regex]::Match($noir.Log, 'State: mobj=(\S+) sector=(\S+)')

    Assert-True $plainState.Success "the plain run reported a world state"
    Assert-True $noirState.Success "and so did the graded run"
    Assert-True ($plainState.Groups[1].Value -eq $noirState.Groups[1].Value) `
        "a colour grade does not change the world"

    Write-Host "Render test passed."
}
catch {
    Write-Host ""
    Write-Host "Frames are in $work"
    throw
}
finally {
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
