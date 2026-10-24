# Captures real engine frames for the README.
#
#   powershell -NoProfile -File tools/capture-game.ps1
#
# The frames come from the headless renderer, which uses exactly the same
# software renderer as the window, so the images are genuine DPT.DOOM output
# without window borders, desktop or personal paths.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Out = "docs/images",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Iwad)) { throw "IWAD not found: $Iwad" }
if (-not (Test-Path $Exe)) { throw "Game executable not found: $Exe. Build it first." }

if (-not (Test-Path $Out)) { New-Item -ItemType Directory -Force $Out | Out-Null }

$temp = Join-Path $env:TEMP "dptdoom-capture"
New-Item -ItemType Directory -Force $temp | Out-Null

function Capture([string]$name, [int]$map, [int]$skill, [int]$tics) {
    $bmp = Join-Path $temp "$name.bmp"
    $png = Join-Path $Out "$name.png"

    & $Exe -headless -iwad $Iwad -warp $map -skill $skill -tics $tics -shot $bmp | Out-Null

    if (-not (Test-Path $bmp)) { throw "The engine did not write $bmp" }

    $image = [System.Drawing.Image]::FromFile($bmp)
    $image.Save((Join-Path (Get-Location) $png), [System.Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()

    Write-Host "Saved $png"
}

# Co-op bots need their own command line, and enough tics for them to walk out of
# the spawn corner: at tic 70 they are still stacked on the player and the frame
# looks like a single-player one.
function Capture-Coop([string]$name, [int]$map, [int]$skill, [int]$bots, [int]$tics) {
    $bmp = Join-Path $temp "$name.bmp"
    $png = Join-Path $Out "$name.png"

    & $Exe -headless -iwad $Iwad -coopbots $bots -warp $map -skill $skill -tics $tics -shot $bmp | Out-Null

    if (-not (Test-Path $bmp)) { throw "The engine did not write $bmp" }

    $image = [System.Drawing.Image]::FromFile($bmp)
    $image.Save((Join-Path (Get-Location) $png), [System.Drawing.Imaging.ImageFormat]::Png)
    $image.Dispose()

    Write-Host "Saved $png"
}

# MAP01: the starting room, classic Doom II opening shot.
Capture "game-map01" 1 3 70

# MAP07: a later map with a different palette of textures.
Capture "game-map07" 7 3 70

# MAP11: the circular arena, a well known Doom II landmark.
Capture "game-map11" 11 3 70

# MAP01 with three co-op bots. Captured from the engine rather than the window so
# the README shows the renderer's own output, with no title bar or menu strip.
Capture-Coop "game-coop-bots" 1 3 3 160

Write-Host "Done."
