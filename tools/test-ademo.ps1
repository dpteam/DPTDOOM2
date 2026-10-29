# Loads DOOM ACE's ademo1.wad and checks that malformed exploit texture data cannot
# prevent the game from reaching a map and advancing tics.
#
#   powershell -NoProfile -File tools/test-ademo.ps1
#
# The WAD is deliberately not asserted to be warning-free. It also uses features this
# engine does not implement yet; those are reported by the loader. The compatibility
# contract checked here is narrower and important: malformed texture records are skipped,
# missing artwork gets a drawable substitute, and the map still loads.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Mod = "refs\ademo1.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [int]$Tics = 8
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
    Write-Host "ADEMO test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Mod)) {
    Write-Host "ADEMO test skipped: $Mod not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-ademo-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$log = Join-Path $work "run.txt"

try {
    & $Exe -headless -iwad $Iwad -file $Mod -warp 1 -skill 3 -tics $Tics `
        -userdir $work -nodata 2>&1 | Set-Content -Path $log -Encoding UTF8

    $text = Get-Content $log -Raw -Encoding UTF8

    Assert-True ($text -like "*textures: OK*") "texture loading completed instead of throwing"
    Assert-True ($text -like "*map MAP01: OK*") "the map loaded"
    Assert-True ($text -like "*Tics: $Tics*") "the game advanced the requested tics"
    Assert-True ($text -notlike "*System.IndexOutOfRangeException*") `
        "malformed texture records did not escape as an index exception"
    Assert-True ($text -notlike "*System.Collections.Generic.KeyNotFoundException*") `
        "missing textures did not escape as a dictionary exception"

    # The frame reader has to resynchronise after a line it does not understand. When it did not,
    # the argument list of a dropped frame was read as sprite names, and one line produced thousands
    # of reports about '(' , ',' and ')' - tokens no mod ever writes as a sprite. These four checks
    # are the shape of that failure, and they are cheap to keep.
    Assert-True ($text -notlike "*no such sprite '('*") "a dropped frame did not leak '(' as a sprite"
    Assert-True ($text -notlike "*no such sprite ')'*") "nor ')'"
    Assert-True ($text -notlike "*no such sprite ','*") "nor ','"
    Assert-True ($text -notlike "*no such sprite '----'*") `
        "the keep-the-previous-sprite marker is understood, not reported as a typo"

    # A ceiling on the report, not a target: it catches a return of the cascading failure without
    # pretending the mod is warning-free. The real number today is well under half of this.
    $decorateProblems = [regex]::Match($text, 'decorate problem: (\d+)')

    Assert-True $decorateProblems.Success "the DECORATE report was produced"
    Assert-True ([int]$decorateProblems.Groups[1].Value -lt 6000) `
        ("the DECORATE report is bounded: " + $decorateProblems.Groups[1].Value + " problems")

    Write-Host "ADEMO test passed."
}
catch {
    Write-Host ""
    Write-Host "Log is in $log"
    throw
}
finally {
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
