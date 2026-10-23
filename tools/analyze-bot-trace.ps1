# Finds the places in a bot trace where the bot walked much further than it got,
# and reports what it was doing there: the decision, the movement input and the
# heading rate. This is the tool for "why does it still circle sometimes".
#
#   powershell -NoProfile -File tools/analyze-bot-trace.ps1 -Trace artifacts/bottrace/coop.json

param(
    [Parameter(Mandatory = $true)][string]$Trace,
    [int]$Window = 24,
    [int]$Step = 6,
    [double]$MinPath = 120,
    [double]$MaxNetRatio = 0.45
)

$ErrorActionPreference = "Stop"

$data = Get-Content $Trace -Raw | ConvertFrom-Json

$series = @{}
foreach ($sample in $data.samples) {
    foreach ($bot in $sample.bots) {
        if (-not $series.ContainsKey($bot.p)) { $series[$bot.p] = New-Object System.Collections.ArrayList }
        [void]$series[$bot.p].Add([pscustomobject]@{
            tic = [int]$sample.tic; x = [double]$bot.x; y = [double]$bot.y
            angle = [double]$bot.angle; fwd = [int]$bot.fwd; side = [int]$bot.side
            turn = [int]$bot.turn; decision = $bot.decision; target = $bot.target
            health = [int]$bot.health
        })
    }
}

foreach ($key in ($series.Keys | Sort-Object)) {
    $points = $series[$key]
    if ($points.Count -lt $Window) { continue }

    Write-Host ""
    Write-Host "=== bot $key ($($points.Count) samples) ==="

    $loops = 0

    for ($i = 0; $i + $Window -lt $points.Count; $i += $Step) {
        $a = $points[$i]
        $b = $points[$i + $Window]

        $path = 0.0
        for ($k = $i + 1; $k -le $i + $Window; $k++) {
            $dx = $points[$k].x - $points[$k - 1].x
            $dy = $points[$k].y - $points[$k - 1].y
            $path += [Math]::Sqrt($dx * $dx + $dy * $dy)
        }

        $dx2 = $b.x - $a.x
        $dy2 = $b.y - $a.y
        $net = [Math]::Sqrt($dx2 * $dx2 + $dy2 * $dy2)

        if ($path -lt $MinPath) { continue }
        if ($net -gt $path * $MaxNetRatio) { continue }

        $loops++

        # What was it doing during this window?
        $decisions = @{}
        $turned = 0.0
        $fwdSum = 0
        $sideSum = 0

        for ($k = $i; $k -le $i + $Window; $k++) {
            $d = $points[$k].decision
            if (-not $decisions.ContainsKey($d)) { $decisions[$d] = 0 }
            $decisions[$d]++
            $turned += [Math]::Abs($points[$k].turn) * 360.0 / 65536.0
            $fwdSum += $points[$k].fwd
            $sideSum += $points[$k].side
        }

        $top = ($decisions.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 2 |
                ForEach-Object { "$($_.Key)=$($_.Value)" }) -join " "

        Write-Host ("  tic {0,5} at {1,6},{2,6}  path {3,5} net {4,4}  turned {5,5} deg  fwd {6,5} side {7,5}  [{8}]  {9}" -f `
            $a.tic, [int]$a.x, [int]$a.y, [int]$path, [int]$net, [int]$turned, `
            [int]($fwdSum / $Window), [int]($sideSum / $Window), $top, $a.target)
    }

    if ($loops -eq 0) { Write-Host "  no loops found" }
    else { Write-Host "  $loops loop windows" }
}
