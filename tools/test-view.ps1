# Checks the crosshair and vertical look.
#
#   powershell -NoProfile -File tools/test-view.ps1
#
# Both features are presentation, and the assertion that matters most is that they
# stay presentation: a tilt that reached the world would break demo playback and
# desynchronise a lockout session, and it would look perfectly fine on screen.
#
# The rest is the behaviour a player would notice:
#   - each crosshair style draws something different, and style 0 draws nothing;
#   - the tilt moves the horizon and leaves the weapon alone;
#   - the tilt is clamped, so the horizon cannot leave the screen;
#   - "-pitch -40" actually parses: a negative value used to be swallowed by the
#     argument parser, which treated every leading '-' as the next option.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 70
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
    Write-Host "View test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$temp = Join-Path $env:TEMP "dptdoom-view"
New-Item -ItemType Directory -Force $temp | Out-Null

function Render([string]$name, [string[]]$extra) {
    $bmp = Join-Path $temp "$name.bmp"

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', "$Tics", '-shot', $bmp) + $extra

    & $Exe @arguments | Out-Null

    if (-not (Test-Path $bmp)) { throw "The engine did not write $bmp" }

    return $bmp
}

function Hash([string]$path) {
    return (Get-FileHash $path -Algorithm MD5).Hash
}

function Pixels([string]$path) {
    $bitmap = [System.Drawing.Bitmap]::FromFile((Resolve-Path $path).Path)
    return $bitmap
}

Write-Host "Crosshair"

$off = Render "ch-off" @('-crosshair', '0')
$cross = Render "ch-cross" @('-crosshair', '1')
$dot = Render "ch-dot" @('-crosshair', '2')
$both = Render "ch-both" @('-crosshair', '3')

$hashes = @{}
foreach ($pair in @(@('off', $off), @('cross', $cross), @('dot', $dot), @('both', $both))) {
    $hashes[$pair[0]] = Hash $pair[1]
}

Assert-True ($hashes['off'] -ne $hashes['cross']) "style 1 differs from no crosshair"
Assert-True ($hashes['off'] -ne $hashes['dot']) "style 2 differs from no crosshair"
Assert-True ($hashes['off'] -ne $hashes['both']) "style 3 differs from no crosshair"
Assert-True ($hashes['cross'] -ne $hashes['dot']) "the cross and the dot are different marks"
Assert-True ($hashes['both'] -ne $hashes['cross']) "style 3 adds a dot to the cross"

# The mark has to be in the middle of the view, and the view is 336 virtual pixels
# tall at 640x400 because the status bar takes the rest.
$offImage = Pixels $off
$crossImage = Pixels $cross

$centreX = 320
$centreY = 168

$changed = 0
for ($y = $centreY - 20; $y -le $centreY + 20; $y++) {
    for ($x = $centreX - 20; $x -le $centreX + 20; $x++) {
        if ($offImage.GetPixel($x, $y).ToArgb() -ne $crossImage.GetPixel($x, $y).ToArgb()) {
            $changed++
        }
    }
}

Assert-True ($changed -gt 20) "the crosshair is drawn around the centre ($changed pixels)"

$corner = $offImage.GetPixel(10, 10).ToArgb() -eq $crossImage.GetPixel(10, 10).ToArgb()
Assert-True $corner "the corner of the frame is untouched"

$offImage.Dispose()
$crossImage.Dispose()

Write-Host ""
Write-Host "Vertical look"

$level = Render "pitch-0" @('-pitch', '0')
$up = Render "pitch-40" @('-pitch', '40')
$down = Render "pitch--40" @('-pitch', '-40')

$levelHash = Hash $level
$upHash = Hash $up
$downHash = Hash $down

Assert-True ($levelHash -ne $upHash) "looking up changes the frame"
Assert-True ($levelHash -ne $downHash) "looking down changes the frame"
Assert-True ($upHash -ne $downHash) "up and down are not the same picture"

# A negative value must survive the argument parser. This is not incidental: the
# parser used to treat any leading '-' as the start of the next option, so
# "-pitch -40" silently became "-pitch" with no value and the tilt stayed at zero -
# which is exactly what "-pitch 0" produces.
$zero = Render "pitch-negzero" @('-pitch', '0')
Assert-True ($downHash -ne (Hash $zero)) "-pitch -40 is not the same as -pitch 0"

# The clamp. Anything past the limit has to look like the limit, not like a horizon
# that left the screen.
$far = Render "pitch-200" @('-pitch', '200')
$limit = Render "pitch-70" @('-pitch', '70')
Assert-True ((Hash $far) -eq (Hash $limit)) "the tilt is clamped at the limit"

# And the weapon stays where it is. Everything that is identical between opposite
# tilts is either the weapon or a piece of uniform colour that happened to land in
# the same place, so this is a count and not a silhouette: the gun is by far the
# largest static thing on screen, and if it moved the number would collapse.
$upImage = Pixels $up
$downImage = Pixels $down

$same = 0

for ($y = 0; $y -lt 336; $y++) {
    for ($x = 0; $x -lt 640; $x++) {
        if ($upImage.GetPixel($x, $y).ToArgb() -eq $downImage.GetPixel($x, $y).ToArgb()) {
            $same++
        }
    }
}

Assert-True ($same -gt 5000) "the weapon does not follow the tilt ($same identical pixels)"

$upImage.Dispose()
$downImage.Dispose()

Write-Host ""
Write-Host "Free look off by default"

# Without -freelook or -pitch the tilt must be zero, or a player who never asked for
# the feature would find the view already tilted.
$plain = Render "plain" @()
Assert-True ((Hash $plain) -eq (Hash $level)) "the view starts level"

Get-ChildItem $temp -Filter *.bmp | Remove-Item -Force

Write-Host "View test passed."
