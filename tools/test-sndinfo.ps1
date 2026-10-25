# Checks SNDINFO in a real run: a sound name plays the lump the file says.
#
#   powershell -NoProfile -File tools/test-sndinfo.ps1
#
# The parser has its own unit tests. What only a real run can show is that the mapping is consulted
# where the sound is actually chosen - and the observable is `-soundtest`, which renders a named
# effect to a WAV. With SNDINFO in play the name asked for and the lump played are no longer the same
# word, so the tool prints both: "DSDOROPN -> DSDORCLS" is the proof.
#
# The lump is written null-padded, which is the WAD format: the reader stops at the first zero, so a
# space-padded name keeps its trailing space and never matches a lookup. "SNDINFO" is seven
# characters, which is exactly the case where that mistake is invisible in the name itself.

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
    Write-Host "SNDINFO test skipped: $Iwad not found."
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "Game executable not found: $Exe. Build it first."
}

$work = Join-Path $env:TEMP ("dptdoom-sndinfo-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null

function New-Pwad([string]$path, [string]$lumpName, [string]$content) {
    $data = [System.Text.Encoding]::ASCII.GetBytes($content)

    $name = New-Object byte[] 8
    $bytes = [System.Text.Encoding]::ASCII.GetBytes($lumpName.ToUpperInvariant())
    [Array]::Copy($bytes, $name, [Math]::Min(8, $bytes.Length))

    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)

    $writer.Write([System.Text.Encoding]::ASCII.GetBytes("PWAD"))
    $writer.Write([int32]1)
    $writer.Write([int32](12 + $data.Length))

    $writer.Write($data)
    $writer.Write([int32]12)
    $writer.Write([int32]$data.Length)
    $writer.Write($name)

    $writer.Flush()
    [System.IO.File]::WriteAllBytes($path, $stream.ToArray())
    $writer.Close()
}

function Sound-Test([string]$name, [string[]]$extra) {
    $wav = Join-Path $work ($name + ".wav")

    $arguments = @('-headless', '-iwad', $Iwad, '-soundtest', $wav, '-sfx', 'doropn',
                   '-userdir', $work, '-nodata') + $extra

    $output = & $Exe @arguments 2>&1

    return [pscustomobject]@{
        Text = ($output -join "`n")
        Wav = $wav
    }
}

try {
    Write-Host "Without SNDINFO"
    $plain = Sound-Test "plain" @()

    Assert-True ($plain.Text -like "*DSDOROPN*") "the conventional lump is used"
    Assert-True ($plain.Text -notlike "*->*") "and nothing was remapped"
    Assert-True (Test-Path $plain.Wav) "a WAV was written"

    Write-Host "With SNDINFO"
    $pwad = Join-Path $work "si.wad"

    # Door opening, mapped to the door-closing recording. Both exist in the IWAD, so the test does
    # not need to ship a sound - and the two are audibly different, so the mapping is a real change
    # rather than a rename.
    New-Pwad $pwad "SNDINFO" @"
// A mod that gives a vanilla sound its own recording.
doropn DSDORCLS
"@

    $mapped = Sound-Test "mapped" @('-file', $pwad)

    Assert-True ($mapped.Text -like "*sndinfo: OK (1 sound(s) described)*") `
        "the lump was found without a flag"
    Assert-True ($mapped.Text -like "*DSDOROPN -> DSDORCLS*") `
        "the name plays the lump the file says"
    Assert-True ($mapped.Text -like "*DSDORCLS (11025 Hz*") "and the sample really is that lump"

    Write-Host "An alias"
    $alias = Join-Path $work "alias.wad"

    New-Pwad $alias "SNDINFO" @"
doropn DSDORCLS
`$alias doornoise doropn
"@

    $wav = Join-Path $work "alias-out.wav"
    $aliasRun = (& $Exe -headless -iwad $Iwad -file $alias -soundtest $wav -sfx doornoise `
        -userdir $work -nodata 2>&1) -join "`n"

    Assert-True ($aliasRun -like "*sndinfo: OK (2 sound(s) described)*") "both entries were read"
    Assert-True ($aliasRun -like "*DSDOORNOISE -> DSDORCLS*") `
        "the alias resolves through the name it points at"

    Write-Host "An unsupported directive"
    $partial = Join-Path $work "partial.wad"

    New-Pwad $partial "SNDINFO" @"
`$volume doropn 0.5
doropn DSDORCLS
"@

    $partialRun = Sound-Test "partial" @('-file', $partial)

    Assert-True ($partialRun.Text -like "*`$volume*") "the unsupported directive is named"
    Assert-True ($partialRun.Text -like "*not a SNDINFO directive*") "and says what it is"
    Assert-True ($partialRun.Text -like "*DSDOROPN -> DSDORCLS*") `
        "while the mapping after it still applies"

    Write-Host "A missing lump"
    $missing = Join-Path $work "missing.wad"

    New-Pwad $missing "SNDINFO" @"
doropn DSNOSUCHLUMP
"@

    $missingRun = Sound-Test "missing" @('-file', $missing)

    Assert-True ($missingRun.Text -like "*DSNOSUCHLUMP*") "the missing lump is named"
    Assert-True ($missingRun.Text -like "*not in this WAD*") "and says it is not there"

    Write-Host "SNDINFO test passed."
}
catch {
    Write-Host ""
    Write-Host "Logs are in $work"
    throw
}
finally {
    if ($Error.Count -eq 0 -and (Test-Path $work)) {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
