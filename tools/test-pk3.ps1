# Checks the archive the engine builds for itself.
#
#   powershell -NoProfile -File tools/test-pk3.ps1
#
# `dptdoom-data/dptdoom.pk3` holds the same actor reference the loose folder holds. The two exist
# for different reasons: the folder is what a person edits, and is never overwritten; the archive is
# one file that can be copied to another machine as-is, and is rewritten when this build's table
# changes.
#
# Two claims, and neither can be checked by a unit test alone:
#
#   - the engine writes a real ZIP. Checked with Windows' own reader, because two implementations
#     of this project agreeing about a file proves they agree, not that the file is a ZIP;
#   - a data directory with the archive and no folder behaves the same as one with the folder.
#     A drift between the two would be invisible on a developer machine, where the folder always
#     exists.

param(
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
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
    Write-Host "PK3 test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-pk3-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function Run-Game([string]$userdir) {
    $output = & $Exe -headless -iwad $Iwad -warp 1 -skill 3 -tics 5 `
                     -userdir $userdir -stateat 5 2>&1
    return ($output -join "`n")
}

function State-Of([string]$text) {
    $match = [regex]::Match($text, 'State@5: mobj=([0-9a-f]+) sector=([0-9a-f]+)')

    if (-not $match.Success) {
        return $null
    }

    return $match.Groups[1].Value + "/" + $match.Groups[2].Value
}

try {
    Write-Host "First run writes the archive"
    $plain = Run-Game $work

    Assert-True ($plain -like "*map MAP01: OK*") "the level loaded"
    Assert-True ($plain -like "*dptdoom.pk3 refreshed*") "and the archive was written"

    $pk3 = Join-Path $work "dptdoom-data\dptdoom.pk3"
    $actors = Join-Path $work "dptdoom-data\actors"

    Assert-True (Test-Path $pk3) "the archive exists"
    Assert-True (Test-Path $actors) "and so does the editable folder"

    Write-Host "Windows reads it as a ZIP"
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::OpenRead($pk3)

    try {
        $names = @($zip.Entries | ForEach-Object { $_.FullName })

        Assert-True ($zip.Entries.Count -eq 4) ("four entries (" + $zip.Entries.Count + ")")
        Assert-True ($names -contains "actors/monsters.dpts") "the actor files are inside"
        Assert-True ($names -contains "actors/README.txt") "and so is the README with the fingerprint"
    }
    finally {
        $zip.Dispose()
    }

    Write-Host "A second run does not rewrite it"
    $second = Run-Game $work

    Assert-True ($second -notlike "*dptdoom.pk3 refreshed*") `
        "the fingerprint matched, so nothing was written"

    # The whole point of a fingerprint check: an unchanged build must not touch the file, or the
    # timestamp lies about when the content last changed.
    $stamp = (Get-Item $pk3).LastWriteTimeUtc

    Start-Sleep -Seconds 1
    $third = Run-Game $work

    Assert-True ($third -notlike "*refreshed*") "still not rewritten"
    Assert-True ((Get-Item $pk3).LastWriteTimeUtc -eq $stamp) "and the file is untouched"

    Write-Host "The archive alone gives the same world as the folder"
    # A fresh directory with the archive copied in and no folder: the case this archive exists for.
    $fromArchive = Join-Path $work "archive-only"
    New-Item -ItemType Directory -Force (Join-Path $fromArchive "dptdoom-data") | Out-Null
    Copy-Item $pk3 (Join-Path $fromArchive "dptdoom-data\dptdoom.pk3")

    $archiveRun = Run-Game $fromArchive

    Assert-True ($archiveRun -like "*map MAP01: OK*") "the level loaded from the archive alone"

    $folderState = State-Of $plain
    $archiveState = State-Of $archiveRun

    Assert-True ($folderState -ne $null) "the folder run produced a world hash"
    Assert-True ($folderState -eq $archiveState) `
        ("and it is the same world ($folderState)")

    Write-Host "PK3 test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    Get-Process -Name dptdoom -ErrorAction SilentlyContinue | Stop-Process -Force

    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
