# Checks the in-game settings screen and the console.
#
#   powershell -NoProfile -File tools/test-console.ps1
#
# Both are reachable without a keyboard: "-showsettings" and "-showconsole" open
# them on the first frame of the level, which is the only way to photograph or
# assert on an overlay from outside the process.
#
# The assertions are about what a player would notice if it broke:
#   - the settings screen draws, and stays inside the 3D view instead of covering
#     the status bar;
#   - the console draws, and its panel does not reach the status bar either;
#   - the console prints its own output;
#   - neither of them changes the world.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 60
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Add-Type -AssemblyName System.Drawing

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Console test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$temp = Join-Path $env:TEMP "dptdoom-console"
New-Item -ItemType Directory -Force $temp | Out-Null

function Render([string]$name, [string[]]$extra) {
    $bmp = Join-Path $temp "$name.bmp"

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', "$Tics", '-shot', $bmp) + $extra

    $output = & $Exe @arguments

    if (-not (Test-Path $bmp)) { throw "The engine did not write $bmp" }

    return @{ Bitmap = $bmp; Log = $output }
}

function Hash([string]$path) {
    return (Get-FileHash $path -Algorithm MD5).Hash
}

Write-Host "Settings screen"

$plain = Render "plain" @()
$settings = Render "settings" @('-showsettings')

Assert-True ((Hash $plain.Bitmap) -ne (Hash $settings.Bitmap)) "the settings screen draws"
Assert-True ($plain.Log.ToString() -eq $settings.Log.ToString()) `
    "the settings screen does not change the world"

# The status bar occupies the bottom 32 virtual pixels, so 64 real pixels at 640x400.
# The panel has to stay above it: a menu that covers the player's health while they
# change the volume is the kind of thing that looks fine in a screenshot and is
# wrong in play.
$image = [System.Drawing.Bitmap]::FromFile((Resolve-Path $settings.Bitmap).Path)
$plainImage = [System.Drawing.Bitmap]::FromFile((Resolve-Path $plain.Bitmap).Path)

$statusTop = $image.Height - 64

# The player's face in the middle of the status bar animates on its own, so it
# differs between any two runs and has nothing to do with the panel. Everything
# else in the status bar is a still image.
$faceLeft = 280
$faceRight = 360

$statusSame = $true
$differing = 0

for ($y = $statusTop; $y -lt $image.Height; $y++) {
    for ($x = 0; $x -lt $image.Width; $x++) {
        if ($x -ge $faceLeft -and $x -le $faceRight) { continue }

        if ($image.GetPixel($x, $y).ToArgb() -ne $plainImage.GetPixel($x, $y).ToArgb()) {
            $differing++
            $statusSame = $false
        }
    }
}

Assert-True $statusSame "the settings panel does not cover the status bar ($differing pixels)"

# And it does cover the middle of the view, or it is not on screen at all.
$centre = $image.GetPixel([int]($image.Width / 2), [int](($image.Height - 64) / 2)).ToArgb()
$centrePlain = $plainImage.GetPixel([int]($plainImage.Width / 2), [int](($plainImage.Height - 64) / 2)).ToArgb()

Assert-True ($centre -ne $centrePlain) "the settings panel is drawn in the middle of the view"

$image.Dispose()
$plainImage.Dispose()

Write-Host ""
Write-Host "Console"

$console = Render "console" @('-showconsole')

Assert-True ((Hash $plain.Bitmap) -ne (Hash $console.Bitmap)) "the console draws"
Assert-True ($plain.Log.ToString() -eq $console.Log.ToString()) `
    "the console does not change the world"

$consoleImage = [System.Drawing.Bitmap]::FromFile((Resolve-Path $console.Bitmap).Path)
$plainImage = [System.Drawing.Bitmap]::FromFile((Resolve-Path $plain.Bitmap).Path)

# The console is an overlay that drops from the *top*, the way Quake's does, so it
# covers the upper part of the view and leaves the bottom visible. The first version
# came up from the bottom and sat on the status bar.
$panelBottom = 232

$bottomSame = $true

for ($y = $panelBottom; $y -lt $consoleImage.Height; $y += 2) {
    for ($x = 0; $x -lt $consoleImage.Width; $x += 5) {
        if ($x -ge $faceLeft -and $x -le $faceRight) { continue }

        if ($consoleImage.GetPixel($x, $y).ToArgb() -ne $plainImage.GetPixel($x, $y).ToArgb()) {
            $bottomSame = $false
            break
        }
    }

    if (-not $bottomSame) { break }
}

Assert-True $bottomSame "the console leaves the bottom of the view visible"

# And it does cover the top, or it is not a drop-down panel.
$topDiffers = $false

for ($y = 2; $y -lt $panelBottom - 20 -and -not $topDiffers; $y += 4) {
    for ($x = 0; $x -lt $consoleImage.Width; $x += 11) {
        if ($consoleImage.GetPixel($x, $y).ToArgb() -ne $plainImage.GetPixel($x, $y).ToArgb()) {
            $topDiffers = $true
            break
        }
    }
}

Assert-True $topDiffers "the console panel covers the top of the screen"

# The panel is dithered rather than solid: every other pixel on both axes is left
# showing the game, so a row differs from the plain frame on exactly half its pixels.
# Asserting "at least a quarter" is what proves the fill happened without pinning
# the pattern, which is a detail of the look rather than of the behaviour.
$panelY = 100

$differs = 0

for ($x = 0; $x -lt $consoleImage.Width; $x++) {
    if ($consoleImage.GetPixel($x, $panelY).ToArgb() -ne $plainImage.GetPixel($x, $panelY).ToArgb()) {
        $differs++
    }
}

Assert-True ($differs -ge ($consoleImage.Width / 4)) `
    "the console panel covers its width ($differs of $($consoleImage.Width) pixels)"

Write-Host ""
Write-Host "The console says something"

# The screen is only useful if it prints. The console now drops from the top, so the
# text is in the upper part of the panel - the lower rows are deliberately left
# showing the game.
$changed = 0

for ($y = 0; $y -lt 60; $y++) {
    for ($x = 0; $x -lt $consoleImage.Width; $x++) {
        if ($consoleImage.GetPixel($x, $y).ToArgb() -ne $plainImage.GetPixel($x, $y).ToArgb()) {
            $changed++
        }
    }
}

$found = $changed -gt 200

Assert-True $found "the console printed text ($changed pixels differ)"

$consoleImage.Dispose()
$plainImage.Dispose()

Get-ChildItem $temp -Filter *.bmp | Remove-Item -Force

Write-Host "Console and settings test passed."
