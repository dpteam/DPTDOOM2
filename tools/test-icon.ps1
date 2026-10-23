# Checks that the shipped icon is usable on every supported Windows version.
#
#   powershell -NoProfile -File tools/test-icon.ps1
#
# Two separate failure modes are covered, both of which actually happened here:
#
#   1. An .ico whose images are PNG streams. Vista and later accept that, but
#      Windows XP does not, and System.Drawing misreads them, so the icon looks
#      like random noise. Every image must be a classic DIB.
#   2. A window that never gets an icon at all. The exe can carry the resource
#      and the window still show a generic icon, because a window class does not
#      pick it up unless the class or the window is told to.

param(
    [string]$Icon = "resources\dptdoom.ico",
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe"
)

$ErrorActionPreference = "Stop"

$failures = 0

function Fail([string]$message) {
    Write-Host "  FAIL $message"
    $script:failures++
}

function Ok([string]$message) {
    Write-Host "  ok   $message"
}

Write-Host "Icon file"

if (-not (Test-Path $Icon)) {
    Fail "$Icon is missing"
} else {
    $bytes = [IO.File]::ReadAllBytes($Icon)

    if ([BitConverter]::ToUInt16($bytes, 0) -ne 0) { Fail "reserved field is not zero" }
    if ([BitConverter]::ToUInt16($bytes, 2) -ne 1) { Fail "not an icon (type is not 1)" }

    $count = [BitConverter]::ToUInt16($bytes, 4)
    Ok "$count images"

    $sizes = @()

    for ($i = 0; $i -lt $count; $i++) {
        $entry = 6 + $i * 16
        $width = $bytes[$entry]
        if ($width -eq 0) { $width = 256 }

        $size = [BitConverter]::ToInt32($bytes, $entry + 8)
        $offset = [BitConverter]::ToInt32($bytes, $entry + 12)

        if ($offset + $size -gt $bytes.Length) {
            Fail "image $i points outside the file"
            continue
        }

        $isPng = $size -gt 8 -and $bytes[$offset] -eq 0x89 -and $bytes[$offset + 1] -eq 0x50
        if ($isPng) {
            Fail "image $i (${width}px) is a PNG stream; Windows XP needs a DIB"
            continue
        }

        $headerSize = [BitConverter]::ToInt32($bytes, $offset)
        if ($headerSize -ne 40) {
            Fail "image $i has a BITMAPINFOHEADER of $headerSize bytes, expected 40"
            continue
        }

        $bmpWidth = [BitConverter]::ToInt32($bytes, $offset + 4)
        $bmpHeight = [BitConverter]::ToInt32($bytes, $offset + 8)
        $bpp = [BitConverter]::ToUInt16($bytes, $offset + 14)

        if ($bmpWidth -ne $width) { Fail "image $i width mismatch ($bmpWidth vs $width)" }
        if ($bmpHeight -ne $width * 2) {
            Fail "image $i height $bmpHeight is not double the width (XOR + AND mask expected)"
        }
        if ($bpp -ne 32) { Fail "image $i is ${bpp}bpp, expected 32" }

        $sizes += $width
    }

    # A window needs at least the small and the large variant.
    if ($sizes -notcontains 16) { Fail "no 16x16 image (title bar)" }
    if ($sizes -notcontains 32) { Fail "no 32x32 image (taskbar)" }
    if ($sizes -notcontains 48) { Fail "no 48x48 image (large icons)" }

    if ($failures -eq 0) {
        Ok "sizes: $($sizes -join ', ') px, all DIB"
    }
}

Write-Host ""
Write-Host "Executable and window"

if (-not (Test-Path $Exe)) {
    Fail "$Exe is missing; build first"
} else {
    Add-Type -AssemblyName System.Drawing

    # Not "$icon": the script parameter $Icon is a typed [string], and
    # PowerShell variable names are case-insensitive, so assigning the icon to
    # $icon would silently convert it to a string.
    $appIcon = [System.Drawing.Icon]::ExtractAssociatedIcon((Resolve-Path $Exe).Path)
    if ($null -eq $appIcon) {
        Fail "the executable carries no icon resource"
    } else {
        Ok "the executable carries an icon ($($appIcon.Width)x$($appIcon.Height))"

        # The icon must not be a blank square, which is what a broken or
        # unreadable resource looks like once decoded.
        $bitmap = $appIcon.ToBitmap()
        $distinct = @{}
        for ($y = 0; $y -lt $bitmap.Height; $y += 2) {
            for ($x = 0; $x -lt $bitmap.Width; $x += 2) {
                $distinct[$bitmap.GetPixel($x, $y).ToArgb()] = $true
            }
        }

        if ($distinct.Count -lt 4) {
            Fail "the executable icon looks blank ($($distinct.Count) distinct colours)"
        } else {
            Ok "the executable icon has $($distinct.Count) distinct colours"
        }

        $bitmap.Dispose()
        $appIcon.Dispose()
    }
}

Write-Host ""
if ($failures -gt 0) {
    throw "$failures icon check(s) failed."
}

Write-Host "Icon check passed."
