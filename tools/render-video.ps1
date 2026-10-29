# Renders a run to a video file, or to a frame sequence ready for an encoder.
#
#   powershell -NoProfile -File tools/render-video.ps1 -Out clip.mp4 -Tics 700
#   powershell -NoProfile -File tools/render-video.ps1 -Out clip.mp4 -Tics 700 -Fps 2 -Res 2
#   powershell -NoProfile -File tools/render-video.ps1 -Out gun.mov -Layer weapon -Key alpha
#   powershell -NoProfile -File tools/render-video.ps1 -Out frames -FramesOnly
#
# The game writes numbered PNGs and a manifest; ffmpeg assembles them. When ffmpeg is missing the
# frames are still written and the command that would have run is printed, so the render mode is
# usable on a machine that has only the game.
#
# Why a script and not a built-in encoder: the game must stay one executable with no third-party
# code, and a video encoder is a large one. Splitting the job at the frame sequence keeps the game
# standalone and lets any tool - ffmpeg, an editor, a compositor - do the encoding.

param(
    [Parameter(Mandatory = $true)]
    [string]$Out,

    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",

    # Simulated tics to run, which is the length of the take: 35 tics is one second.
    [int]$Tics = 350,

    # Tics to simulate before recording. The level-start wipe takes a variable number of updates,
    # so a take that begins at frame zero cannot be repeated.
    [int]$Skip = 35,

    # Frames per tic: 1 is 35 fps, 2 is 70, 3 is 105.
    [int]$Fps = 1,

    # Resolution multiplier over 320x200.
    [int]$Res = 1,

    [ValidateSet("full", "world", "weapon", "hud")]
    [string]$Layer = "full",

    [ValidateSet("green", "alpha")]
    [string]$Key = "green",

    [ValidateSet("", "night", "noir", "sepia", "vivid")]
    [string]$Grade = "",

    [int]$Pitch = 0,
    [int]$Yaw = 0,
    [int]$Warp = 1,
    [int]$Skill = 3,

    # Extra arguments passed to the game, such as -file a.wad.
    [string[]]$GameArgs = @(),

    # Write the frames and stop, without encoding.
    [switch]$FramesOnly,

    # Constant Rate Factor for the encoder; lower is better and bigger.
    [int]$Crf = 18,

    # Keep the frames next to the video instead of a temporary directory.
    [switch]$KeepFrames
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path $Iwad)) {
    throw "IWAD not found: $Iwad"
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$framesOnly = $FramesOnly -or $Out -notmatch '\.(mp4|mov|mkv|webm|gif)$'

if ($framesOnly) {
    $frames = $Out
}
elseif ($KeepFrames) {
    $frames = [System.IO.Path]::ChangeExtension($Out, $null).TrimEnd('.') + "_frames"
}
else {
    $frames = Join-Path $env:TEMP ("dptdoom-frames-" + [Guid]::NewGuid().ToString("N"))
}

New-Item -ItemType Directory -Force $frames | Out-Null

$arguments = @(
    '-render', $frames,
    '-renderfps', "$Fps",
    '-renderres', "$Res",
    '-renderskip', "$Skip",
    '-tics', "$Tics",
    '-iwad', $Iwad,
    '-warp', "$Warp",
    '-skill', "$Skill",
    '-nodata'
)

if ($Layer -ne 'full') {
    $arguments += @('-renderlayer', $Layer, '-renderkey', $Key)
}

if ($Grade -ne '') {
    $arguments += @('-grade', $Grade)
}

if ($Pitch -ne 0) {
    $arguments += @('-pitch', "$Pitch")
}

if ($Yaw -ne 0) {
    $arguments += @('-yaw', "$Yaw")
}

if ($GameArgs.Count -gt 0) {
    $arguments += $GameArgs
}

Write-Host "Rendering $Tics tics at $Fps frame(s) per tic..."

$log = & $Exe @arguments 2>&1 | Out-String

foreach ($line in ($log -split "`r?`n")) {
    if ($line -match '^(Rendered|Sequence|State|map |Warning: decorate problem)') {
        Write-Host "  $line"
    }
}

$manifest = Join-Path $frames 'render.txt'

if (-not (Test-Path $manifest)) {
    throw "The render produced no manifest. Log:`n$log"
}

$values = @{}

foreach ($line in Get-Content $manifest) {
    if ($line -match '^\s*([a-z]+)\s*=\s*(.*)$') {
        $values[$matches[1]] = $matches[2].Trim()
    }
}

$frameCount = [int]$values['frames']
$fpsValue = $values['fps']
$width = $values['width']
$height = $values['height']

Write-Host "Frames: $frameCount at $fpsValue fps, ${width}x${height}"

if ($frameCount -eq 0) {
    throw "The render produced no frames."
}

if ($framesOnly) {
    Write-Host "Frames are in $frames"
    exit 0
}

$ffmpeg = Get-Command ffmpeg -ErrorAction SilentlyContinue

# A keyed layer is a cut-out: it has to keep its alpha, so it is encoded in a format that carries
# one. Anything else is ordinary footage.
$isKeyed = $Layer -ne 'full' -and $Key -eq 'alpha'

$encode = @(
    '-y',
    '-framerate', $fpsValue,
    '-i', (Join-Path $frames 'frame_%05d.png')
)

if ($isKeyed) {
    # qtrle carries straight alpha and every editor reads it; the alternative is to flatten the
    # cut-out onto a colour and lose the point of rendering it separately.
    $encode += @('-c:v', 'qtrle')
}
else {
    $encode += @(
        '-c:v', 'libx264',
        '-preset', 'slow',
        '-crf', "$Crf",
        '-pix_fmt', 'yuv420p',
        # Even dimensions are what every player expects; the renderer already produces them, but a
        # frame size of 320x200 scaled oddly would not.
        '-vf', 'scale=trunc(iw/2)*2:trunc(ih/2)*2'
    )
}

$encode += @($Out)

if (-not $ffmpeg) {
    Write-Host ""
    Write-Host "ffmpeg is not on PATH, so nothing was encoded. Frames are in $frames"
    Write-Host "The command that would have run:"
    Write-Host ("  ffmpeg " + ($encode -join ' '))
    exit 0
}

Write-Host "Encoding with ffmpeg..."

# All streams to a log, not to the console. ffmpeg writes its progress to stderr, and with
# ErrorActionPreference = Stop that becomes an error record - the script then exits non-zero on a
# successful encode, which is exactly the kind of bug that makes an automation wrapper useless.
$ffmpegLog = Join-Path $env:TEMP ("dptdoom-ffmpeg-" + [Guid]::NewGuid().ToString("N") + ".txt")

# ErrorActionPreference has to be relaxed for the call itself: in Windows PowerShell 5.1 a native
# program writing to stderr becomes a terminating error under "Stop", even when the streams are
# redirected. That is why a successful encode used to leave the script exiting 1.
$saved = $ErrorActionPreference
$ErrorActionPreference = 'Continue'

& ffmpeg @encode *> $ffmpegLog

$code = $LASTEXITCODE
$ErrorActionPreference = $saved

if ($code -ne 0) {
    Get-Content $ffmpegLog | Select-Object -Last 20 | ForEach-Object { Write-Host "  $_" }
    Remove-Item $ffmpegLog -Force -ErrorAction SilentlyContinue
    throw "ffmpeg failed with exit code $code"
}

Remove-Item $ffmpegLog -Force -ErrorAction SilentlyContinue

if (-not $KeepFrames) {
    Remove-Item $frames -Recurse -Force -ErrorAction SilentlyContinue
}

$size = (Get-Item $Out).Length / 1MB
Write-Host ("Wrote {0} ({1:N1} MB)" -f $Out, $size)
