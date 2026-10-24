# Checks the render profiler: that it reports, and above all that it changes nothing.
#
#   powershell -NoProfile -File tools/test-profile.ps1
#
# A profiler that alters what it measures is worse than no profiler. This one sits
# inside the column routines and the frame boundaries, so it can plausibly change
# both the picture and the simulation - the renderer marks lines as seen for the
# automap and takes valid counts from the world, and doing that every tic instead
# of never is exactly the kind of thing that shifts a hash.
#
# So the assertions are, in order of importance:
#
#   - the rendered frame is byte-identical with and without -profile;
#   - the world hash is identical with and without -profile;
#   - the sections account for the whole frame (a section missing from the sum is
#     a section nobody can optimize);
#   - the report is actually populated, so a silent no-op cannot pass.
#
# Locale note: the report is written with the invariant culture, so it always says
# "1.82" and never "1,82". Parsing therefore uses the invariant culture explicitly
# rather than the machine's, which is how the first version of this script managed
# to read every number as zero on a Russian system.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 300
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$invariant = [Globalization.CultureInfo]::InvariantCulture

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) {
        Write-Host "  FAIL $message"
        throw $message
    }

    Write-Host "  ok   $message"
}

function Parse-Number([string]$text) {
    return [double]::Parse($text, $invariant)
}

if (-not (Test-Path $Iwad)) {
    Write-Host "Profile test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$plainBmp = Join-Path $env:TEMP "profile-plain.bmp"
$profBmp = Join-Path $env:TEMP "profile-on.bmp"
Remove-Item $plainBmp, $profBmp -ErrorAction SilentlyContinue

$plain = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics -shot $plainBmp 2>&1
$prof = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics $Tics -shot $profBmp -profile 2>&1

$plainState = ($plain | Select-String 'State: mobj=' | Select-Object -First 1).ToString()
$profState = ($prof | Select-String 'State: mobj=' | Select-Object -First 1).ToString()

Assert-True ($null -ne $plainState -and $null -ne $profState) "both runs simulated a world"
Assert-True ($plainState -eq $profState) `
    "profiling does not change the simulation ($profState)"

$plainHash = (Get-FileHash $plainBmp -Algorithm MD5).Hash
$profHash = (Get-FileHash $profBmp -Algorithm MD5).Hash
Assert-True ($plainHash -eq $profHash) "profiling does not change the rendered frame ($profHash)"

$header = $prof | Select-String -SimpleMatch 'Render profile over' | Select-Object -First 1
Assert-True ($null -ne $header) "the report was printed"

$frameMs = Parse-Number ([regex]::Match($header.ToString(), '\(([0-9.]+) ms per frame').Groups[1].Value)
Assert-True ($frameMs -gt 0) "the frame took measurable time ($frameMs ms)"

$sections = @{}
foreach ($name in @('setup', 'bsp', 'walls', 'flats', 'sprites', 'masked', 'player', 'sky', 'convert', 'hud')) {
    $line = $prof | Select-String -Pattern ("^\s+" + $name + "\s+[0-9]") | Select-Object -First 1
    Assert-True ($null -ne $line) "the report has a '$name' section"

    $value = [regex]::Match($line.ToString(), '([0-9]+\.[0-9]+) ms').Groups[1].Value
    $sections[$name] = Parse-Number $value
}

# Walls and flats are drawn inside the BSP traversal, so only the top-level
# sections belong in the sum. If that ever stops being true the report would
# double count, and the total below would exceed the frame.
$topLevel = @('setup', 'bsp', 'sprites', 'masked', 'player', 'sky', 'convert', 'hud')
$sum = 0.0
foreach ($name in $topLevel) {
    $sum += $sections[$name]
}

Assert-True ($sum -gt ($frameMs * 0.8)) `
    ("the sections account for the frame ({0:0.00} of {1:0.00} ms)" -f $sum, $frameMs)

Assert-True ($sum -le ($frameMs * 1.15)) `
    ("no section is counted twice ({0:0.00} of {1:0.00} ms)" -f $sum, $frameMs)

Assert-True ($sections['walls'] -le ($sections['bsp'] * 1.01)) `
    ("wall columns are part of the BSP traversal ({0:0.00} within {1:0.00})" -f $sections['walls'], $sections['bsp'])

Assert-True ($sections['flats'] -le ($sections['bsp'] * 1.01)) `
    ("flat columns are part of the BSP traversal ({0:0.00} within {1:0.00})" -f $sections['flats'], $sections['bsp'])

$countersLine = $prof | Select-String -SimpleMatch 'per frame:' | Select-Object -First 1
Assert-True ($null -ne $countersLine) "the report counts what was drawn ($countersLine)"

foreach ($counter in @('segs', 'wall columns', 'flat columns')) {
    $match = [regex]::Match($countersLine.ToString(), [regex]::Escape($counter) + ' ([0-9.]+)')
    Assert-True ($match.Success) "the report counts '$counter'"
    Assert-True ((Parse-Number $match.Groups[1].Value) -gt 0) "'$counter' is not zero"
}

Remove-Item $plainBmp, $profBmp -ErrorAction SilentlyContinue

Write-Host "Render profile test passed."
