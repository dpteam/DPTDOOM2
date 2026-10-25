# Checks ROFL mode in a real run: the frame is mirrored, and the world is not.
#
#   powershell -NoProfile -File tools/test-rofl.ps1
#
# The two claims that matter, and they pull in opposite directions:
#
#   - the picture must actually be mirrored, or the mode does nothing;
#   - the world must be **unchanged**, because that is what makes the mode safe: a demo
#     recorded with it on replays exactly, and lockstep never has to know it exists.
#
# A headless run renders through the same renderer as the window and can write a frame, so both
# claims are checked from the outside: compare two BMPs pixel by pixel, and compare the world
# hash of a run with the mode on against one with it off.
#
# What is *not* checked here is the Konami code and the April 1 default - those are pure logic
# and live in KonamiCodeTest and RoflModeTest, where a clock can be handed in instead of waited
# for.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 40
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
    Write-Host "ROFL test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-rofl-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

# Each run gets its own config, because ROFL mode is stored there and a run that inherited the
# developer's file would test the developer's machine.
function Run-Game([string]$name, [bool]$rofl) {
    $dir = Join-Path $work $name
    New-Item -ItemType Directory -Force $dir | Out-Null

    if ($rofl) {
        # Written the way the game writes it: only when the player has chosen.
        Set-Content -Path (Join-Path $dir "dptdoom.cfg") `
            -Value "rofl_mode = true`r`nrofl_set = true" -Encoding ASCII
    }

    $shot = Join-Path $dir "frame.bmp"

    $output = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics `
        -userdir $dir -shot $shot 2>&1

    return [pscustomobject]@{
        Text = ($output -join "`n")
        Shot = $shot
        Dir = $dir
    }
}

# Reads a BMP written by the game, and the bytes of one row of it.
#
# The game writes 24 bits per pixel, so a "pixel" is three bytes and comparing single bytes
# compares colour channels - which passes for any two frames with similar shading and proves
# nothing. The stride is derived from the file rather than assumed, because the width in pixels
# and the width in bytes are different numbers.
function Read-Bmp([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)

    $dataOffset = [BitConverter]::ToInt32($bytes, 10)
    $width = [BitConverter]::ToInt32($bytes, 18)
    $height = [BitConverter]::ToInt32($bytes, 22)

    $pixels = New-Object byte[] ($bytes.Length - $dataOffset)
    [Array]::Copy($bytes, $dataOffset, $pixels, 0, $pixels.Length)

    $stride = [int](($bytes.Length - $dataOffset) / $height)

    return [pscustomobject]@{
        Width = $width
        Height = $height
        Pixels = $pixels
        Stride = $stride
        Bpp = [int]($stride / $width)
    }
}

# Whether the pixel at (x, y) of one frame equals the pixel at (mx, y) of another.
function Pixel-Matches($a, $b, [int]$x, [int]$mx, [int]$y) {
    $i = $y * $a.Stride + $x * $a.Bpp
    $j = $y * $b.Stride + $mx * $b.Bpp

    return ($a.Pixels[$i] -eq $b.Pixels[$j] -and
            $a.Pixels[$i + 1] -eq $b.Pixels[$j + 1] -and
            $a.Pixels[$i + 2] -eq $b.Pixels[$j + 2])
}

try {
    Write-Host "A frame without the mode"
    $plain = Run-Game "plain" $false

    Assert-True ($plain.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True (Test-Path $plain.Shot) "a frame was written"

    Write-Host "A frame with the mode"
    $rofl = Run-Game "rofl" $true

    Assert-True ($rofl.Text -like "*map MAP01: OK*") "the level loaded"
    Assert-True (Test-Path $rofl.Shot) "a frame was written"

    $a = Read-Bmp $plain.Shot
    $b = Read-Bmp $rofl.Shot

    Assert-True ($a.Width -eq $b.Width -and $a.Height -eq $b.Height) `
        ("the frames are the same size ($($a.Width)x$($a.Height))")

    # The middle row is where the view is: a mirror shows up there immediately.
    #
    # The BMP is bottom-up (biHeight positive), so array row 0 is the *bottom* of the picture.
    # That is not a detail to skip: reading it as top-down made an earlier version of this
    # script check the status bar for mirroring and the view for sameness, and both came out
    # inverted while the renderer was correct.
    $viewRow = 200

    $different = 0
    $mirrored = 0

    for ($x = 0; $x -lt $a.Width; $x++) {
        if (-not (Pixel-Matches $a $b $x $x $viewRow)) {
            $different++
        }

        # Every column of the plain frame should equal the mirror-image column of the ROFL one.
        if (Pixel-Matches $a $b $x ($a.Width - 1 - $x) $viewRow) {
            $mirrored++
        }
    }

    Assert-True ($different -gt ($a.Width / 4)) `
        ("the picture really is different ({0} of {1} columns)" -f $different, $a.Width)

    Assert-True ($mirrored -gt ($a.Width * 0.9)) `
        ("and it is mirrored, not merely different ({0} of {1} columns match their mirror)" -f
            $mirrored, $a.Width)

    # The status bar sits below the view window and must stay readable: it is drawn after the
    # mirror. In a bottom-up BMP it is the first rows of the array.
    $barSame = 0
    $barTotal = 0

    for ($row = 0; $row -lt 60; $row++) {
        for ($x = 0; $x -lt $a.Width; $x++) {
            $barTotal++

            if (Pixel-Matches $a $b $x $x $row) {
                $barSame++
            }
        }
    }

    Assert-True ($barSame -gt ($barTotal * 0.9)) `
        ("the status bar is not mirrored ({0} of {1} pixels unchanged)" -f $barSame, $barTotal)

    Write-Host "The world"
    # The claim that makes the mode safe: the simulation does not know it exists. Both runs are
    # the same input, so the world hash has to match to the last bit.
    $hashA = [regex]::Match($plain.Text, 'State: mobj=(\S+) sector=(\S+) tic=(\d+)')
    $hashB = [regex]::Match($rofl.Text, 'State: mobj=(\S+) sector=(\S+) tic=(\d+)')

    Assert-True $hashA.Success "the plain run reported a world state"
    Assert-True $hashB.Success "and so did the mirrored one"

    Assert-True ($hashA.Value -eq $hashB.Value) `
        ("the world is identical with the mode on and off (" + $hashA.Value + " vs " + $hashB.Value + ")")

    Write-Host "ROFL test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
