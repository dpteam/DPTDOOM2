# Renders a bot trace into a PNG: the level geometry, the path each bot walked,
# its heading at every sample, and the places where it went round in circles.
#
#   powershell -NoProfile -File tools/render-bot-trace.ps1 `
#       -Trace artifacts/bottrace/map01-bots.json -Out artifacts/bottrace/map01-bots.png
#
# The trace itself is written by the game:
#
#   dptdoom.exe -headless -iwad doom2.wad -bots -warp 1 -skill 3 -tics 3000 `
#       -botdump artifacts/bottrace/map01-bots.json
#
# Two files come out: the PNG for looking at, and a .summary.json next to it with
# the numbers behind the picture — how much of the run was spent turning on the
# spot, how many circles were detected, and where.

param(
    [Parameter(Mandatory = $true)][string]$Trace,
    [string]$Out = "",
    [int]$Scale = 2,
    [int]$ArrowEvery = 20,
    [int]$CircleRadius = 70
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not (Test-Path $Trace)) { throw "Trace not found: $Trace" }
if ($Out -eq "") { $Out = [IO.Path]::ChangeExtension($Trace, ".png") }

$data = Get-Content $Trace -Raw | ConvertFrom-Json

# ---------------------------------------------------------------- geometry

$minX = [double]::MaxValue; $maxX = [double]::MinValue
$minY = [double]::MaxValue; $maxY = [double]::MinValue

foreach ($line in $data.lines) {
    foreach ($x in @($line.x1, $line.x2)) {
        if ($x -lt $minX) { $minX = $x }
        if ($x -gt $maxX) { $maxX = $x }
    }
    foreach ($y in @($line.y1, $line.y2)) {
        if ($y -lt $minY) { $minY = $y }
        if ($y -gt $maxY) { $maxY = $y }
    }
}

$pad = 64
$width = [int](($maxX - $minX + 2 * $pad) * $Scale)
$height = [int](($maxY - $minY + 2 * $pad) * $Scale)

# Keep the image a sane size for a README or a bug report.
$maxSide = 2400
if ($width -gt $maxSide -or $height -gt $maxSide) {
    $shrink = [Math]::Min($maxSide / $width, $maxSide / $height)
    $Scale = $Scale * $shrink
    $width = [int](($maxX - $minX + 2 * $pad) * $Scale)
    $height = [int](($maxY - $minY + 2 * $pad) * $Scale)
}

# Doom's Y axis points up the map, screen Y points down.
function To-ScreenX([double]$x) { return [int](($x - $minX + $pad) * $Scale) }
function To-ScreenY([double]$y) { return [int](($maxY - $y + $pad) * $Scale) }

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$g = [System.Drawing.Graphics]::FromImage($bitmap)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.Clear([System.Drawing.Color]::FromArgb(18, 20, 24))

$solidPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(96, 104, 116), 1.0)
$twoSidedPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(48, 54, 64), 1.0)
$exitPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(120, 220, 120), 1.6)
$doorPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(200, 170, 90), 1.2)

$exitSpecials = @(11, 51, 52, 124)
$doorSpecials = @(1, 26, 27, 28, 31, 32, 33, 34, 117, 118)

foreach ($line in $data.lines) {
    $special = [int]$line.special

    if ($exitSpecials -contains $special) { $pen = $exitPen }
    elseif ($doorSpecials -contains $special) { $pen = $doorPen }
    elseif ($line.two) { $pen = $twoSidedPen }
    else { $pen = $solidPen }

    $g.DrawLine($pen,
        (To-ScreenX $line.x1), (To-ScreenY $line.y1),
        (To-ScreenX $line.x2), (To-ScreenY $line.y2))
}

# ---------------------------------------------------------------- samples

# One series per bot, in the order they appear.
$series = @{}

foreach ($sample in $data.samples) {
    foreach ($bot in $sample.bots) {
        if (-not $series.ContainsKey($bot.p)) { $series[$bot.p] = New-Object System.Collections.ArrayList }
        [void]$series[$bot.p].Add([pscustomobject]@{
            tic    = [int]$sample.tic
            x      = [double]$bot.x
            y      = [double]$bot.y
            angle  = [double]$bot.angle
            fwd    = [int]$bot.fwd
            side   = [int]$bot.side
            turn   = [int]$bot.turn
            health = [int]$bot.health
            sector = [int]$bot.sector
            style  = $bot.style
        })
    }
}

$palette = @(
    [System.Drawing.Color]::FromArgb(90, 190, 255),
    [System.Drawing.Color]::FromArgb(255, 170, 80),
    [System.Drawing.Color]::FromArgb(160, 240, 130),
    [System.Drawing.Color]::FromArgb(240, 120, 200)
)

$report = @()
$colourIndex = 0

foreach ($key in ($series.Keys | Sort-Object)) {
    $points = $series[$key]
    if ($points.Count -lt 2) { continue }

    $colour = $palette[$colourIndex % $palette.Count]
    $colourIndex++

    # Path: brightness follows speed, so a bot that stops is a dark stretch and
    # a bot that runs is a bright one.
    for ($i = 1; $i -lt $points.Count; $i++) {
        $a = $points[$i - 1]
        $b = $points[$i]

        $dx = $b.x - $a.x
        $dy = $b.y - $a.y
        $speed = [Math]::Sqrt($dx * $dx + $dy * $dy)

        $t = [Math]::Min(1.0, $speed / 12.0)
        $r = [int]($colour.R * (0.35 + 0.65 * $t))
        $gg = [int]($colour.G * (0.35 + 0.65 * $t))
        $bb = [int]($colour.B * (0.35 + 0.65 * $t))

        $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb($r, $gg, $bb), 1.4)
        $g.DrawLine($pen, (To-ScreenX $a.x), (To-ScreenY $a.y), (To-ScreenX $b.x), (To-ScreenY $b.y))
        $pen.Dispose()
    }

    # Heading arrows: where the bot was looking, not where it was going. The two
    # differing is exactly what a circle looks like.
    $arrowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(220, $colour), 1.0)

    for ($i = 0; $i -lt $points.Count; $i += $ArrowEvery) {
        $p = $points[$i]
        $rad = $p.angle * [Math]::PI / 180.0

        $len = 22
        $ex = $p.x + [Math]::Cos($rad) * $len
        $ey = $p.y + [Math]::Sin($rad) * $len

        $g.DrawLine($arrowPen, (To-ScreenX $p.x), (To-ScreenY $p.y), (To-ScreenX $ex), (To-ScreenY $ey))

        $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(230, $colour))),
            (To-ScreenX $p.x) - 1, (To-ScreenY $p.y) - 1, 3, 3)
    }

    $arrowPen.Dispose()

    # Start and end.
    $first = $points[0]
    $last = $points[$points.Count - 1]

    $g.FillEllipse([System.Drawing.Brushes]::White,
        (To-ScreenX $first.x) - 4, (To-ScreenY $first.y) - 4, 8, 8)

    $g.FillRectangle([System.Drawing.Brushes]::Red,
        (To-ScreenX $last.x) - 4, (To-ScreenY $last.y) - 4, 8, 8)

    # ------------------------------------------------------------ circling
    #
    # A circle is detected by geometry, not by eye: walk the path in windows,
    # and if the start and end of the window are close together while the path
    # between them is much longer, the bot went round. The turn spent is summed
    # too, because orbiting also means the heading never settles.

    $circles = New-Object System.Collections.ArrayList
    $window = 24

    for ($i = 0; $i + $window -lt $points.Count; $i += 6) {
        $a = $points[$i]
        $b = $points[$i + $window]

        $path = 0.0
        for ($k = $i + 1; $k -le $i + $window; $k++) {
            $dx = $points[$k].x - $points[$k - 1].x
            $dy = $points[$k].y - $points[$k - 1].y
            $path += [Math]::Sqrt($dx * $dx + $dy * $dy)
        }

        $dx2 = $b.x - $a.x
        $dy2 = $b.y - $a.y
        $net = [Math]::Sqrt($dx2 * $dx2 + $dy2 * $dy2)

        if ($path -gt 160 -and $net -lt $path * 0.45) {
            [void]$circles.Add([pscustomobject]@{
                tic   = $a.tic
                x     = $a.x
                y     = $a.y
                path  = [Math]::Round($path)
                net   = [Math]::Round($net)
            })
        }
    }

    $circlePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 70, 70), 1.6)

    foreach ($c in $circles) {
        $g.DrawEllipse($circlePen,
            (To-ScreenX $c.x) - 18, (To-ScreenY $c.y) - 18, 36, 36)
    }

    $circlePen.Dispose()

    # ------------------------------------------------------------ numbers

    $idle = 0
    $strafeOnly = 0
    $turned = 0.0

    foreach ($p in $points) {
        if ($p.fwd -eq 0 -and $p.side -eq 0) { $idle++ }
        if ($p.fwd -eq 0 -and $p.side -ne 0) { $strafeOnly++ }
        $turned += [Math]::Abs($p.turn) * 360.0 / 65536.0
    }

    $report += [pscustomobject]@{
        player        = $key
        style         = $first.style
        samples       = $points.Count
        idleTics      = $idle
        idlePercent   = [Math]::Round(100.0 * $idle / $points.Count, 1)
        strafeOnly    = $strafeOnly
        strafePercent = [Math]::Round(100.0 * $strafeOnly / $points.Count, 1)
        turnedDegrees = [Math]::Round($turned)
        circles       = $circles.Count
        circlePlaces  = ($circles | ForEach-Object { "$($_.x.ToString('0')),$($_.y.ToString('0'))@$($_.tic)" }) -join " "
        health        = $last.health
    }
}

# ---------------------------------------------------------------- legend

$font = New-Object System.Drawing.Font("Consolas", 11)
$titleFont = New-Object System.Drawing.Font("Consolas", 14, [System.Drawing.FontStyle]::Bold)

$g.FillRectangle((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(210, 12, 14, 18))), 8, 8, 470, 28 + 18 * ($report.Count + 3))

$g.DrawString("$($data.map)  skill $($data.skill)  tics $($data.levelTime)", $titleFont,
    [System.Drawing.Brushes]::White, 16, 12)

$y = 40
$g.DrawString("brightness = speed, arrows = heading, red ring = circling", $font,
    (New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(170, 180, 195))), 16, $y)
$y += 20

foreach ($r in $report) {
    $line = "bot $($r.player) $($r.style): idle $($r.idlePercent)%  strafe-only $($r.strafePercent)%  circles $($r.circles)  turned $($r.turnedDegrees)deg"
    $g.DrawString($line, $font, [System.Drawing.Brushes]::White, 16, $y)
    $y += 18
}

$g.Dispose()

$bitmap.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

# ---------------------------------------------------------------- summary

$summary = [pscustomobject]@{
    map     = $data.map
    skill   = $data.skill
    tics    = $data.levelTime
    png     = $Out
    trace   = $Trace
    bots    = $report
}

$summaryPath = [IO.Path]::ChangeExtension($Out, ".summary.json")
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $summaryPath -Encoding UTF8

Write-Host "Wrote $Out"
Write-Host "Wrote $summaryPath"
foreach ($r in $report) {
    Write-Host ("  bot {0} {1}: idle {2}%  strafe-only {3}%  circles {4}  turned {5} deg" -f `
        $r.player, $r.style, $r.idlePercent, $r.strafePercent, $r.circles, $r.turnedDegrees)
}
