# Checks that the automap debug overlays actually draw something.
#
#   powershell -NoProfile -File tools/test-debug-overlays.ps1
#
# Why this exists
# ---------------
# "-maponly" was in the command line and in the menu, and it did nothing: the flag
# was applied only by the window's own toggle, so a headless run rendered the 3D view
# and every screenshot of a debug overlay was byte-identical to the plain frame. The
# overlays themselves could have been complete and unobservable.
#
# The check is the cheapest thing that is still honest: render the same moment four
# times with different flags and require the frames to differ, and require the plain
# frame to be reproduced exactly when the flags are off. A toggle that changes
# nothing fails; nondeterminism fails too.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 40
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Iwad)) {
    Write-Host "Debug overlay test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$temp = Join-Path $env:TEMP ("dptdoom-overlays-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $temp | Out-Null

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

function Render([string]$name, [string[]]$extra) {
    $path = Join-Path $temp "$name.bmp"

    $arguments = @('-headless', '-iwad', $Iwad, '-warp', '1', '-skill', '3',
                   '-tics', "$Tics", '-shot', $path) + $extra

    & $Exe @arguments | Out-Null

    if (-not (Test-Path $path)) {
        throw "no frame was written for '$name'"
    }

    return (Get-FileHash $path -Algorithm MD5).Hash
}

try {
    Write-Host "Automap debug overlays:"

    $plain = Render "plain" @()
    $repeat = Render "repeat" @()

    Assert-True ($plain -eq $repeat) "the same command produces the same frame"

    $mapOnly = Render "maponly" @('-maponly')
    Assert-True ($mapOnly -ne $plain) "-maponly renders the automap instead of the 3D view"

    $triggers = Render "triggers" @('-maponly', '-showtriggers')
    Assert-True ($triggers -ne $mapOnly) "-showtriggers changes the automap"

    $blockmap = Render "blockmap" @('-maponly', '-showblockmap')
    Assert-True ($blockmap -ne $mapOnly) "-showblockmap changes the automap"

    Assert-True ($triggers -ne $blockmap) "the two overlays are not the same drawing"

    # And they must not leak: a run without the flags is unaffected by the previous
    # one. Debug state is static, so this is a real risk rather than a formality.
    $after = Render "after" @()
    Assert-True ($after -eq $plain) "overlay flags do not persist between runs"

    Write-Host "Debug overlay test passed."
}
finally {
    Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
}
