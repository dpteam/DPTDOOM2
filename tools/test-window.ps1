# Checks that the Win32 front end creates a window with the right title.
#
#   powershell -NoProfile -File tools/test-window.ps1
#
# This is a regression test for a real bug: DefWindowProc was declared without
# CharSet, so P/Invoke picked the ANSI variant, and every WM_SETTEXT was decoded
# as ANSI. The window title arrived as "D" instead of "DPT.DOOM v0.1.0".
# Any window message that carries a string would have been mangled the same way.

param(
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [string]$Iwad = "refs\iwads\doom2.wad"
)

$ErrorActionPreference = "Stop"

# Without an IWAD the game exits before it creates a window, so this check
# cannot run. CI has no game data; the unit test Win32WindowTest covers the same
# bug without needing one.
if (-not (Test-Path $Iwad)) {
    Write-Host "Skipped: no IWAD at $Iwad (see DPTDOOMTest/src/UnitTests/Win32WindowTest.cs)."
    exit 0
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DptWindowCheck {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
}
"@

if (-not (Test-Path $Exe)) { throw "Game executable not found: $Exe" }
if (-not (Test-Path $Iwad)) { throw "IWAD not found: $Iwad" }

$process = Start-Process -FilePath $Exe -ArgumentList '-iwad', $Iwad, '-warp', '1' -PassThru

try {
    $deadline = (Get-Date).AddSeconds(30)
    $title = $null

    while ((Get-Date) -lt $deadline -and $null -eq $title) {
        Start-Sleep -Milliseconds 400

        $script:found = $null
        $callback = [DptWindowCheck+EnumProc] {
            param($h, $l)

            $owner = 0
            [void][DptWindowCheck]::GetWindowThreadProcessId($h, [ref]$owner)

            if ($owner -ne $process.Id) { return $true }

            $className = New-Object System.Text.StringBuilder 256
            [void][DptWindowCheck]::GetClassNameW($h, $className, 256)

            if ($className.ToString() -ne "DPTDOOMWindow") { return $true }

            $text = New-Object System.Text.StringBuilder 256
            [void][DptWindowCheck]::GetWindowTextW($h, $text, 256)
            $script:found = $text.ToString()

            return $false
        }

        [void][DptWindowCheck]::EnumWindows($callback, [IntPtr]::Zero)

        if ($script:found) { $title = $script:found }
    }

    if ($null -eq $title) { throw "The game window did not appear." }

    Write-Host "Window title: '$title'"

    if ($title -ne "DPT.DOOM v0.1.0") {
        throw "FAILED: the window title is mangled ('$title'). Check the CharSet/EntryPoint of DefWindowProc."
    }

    Write-Host "Window title test passed."
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
}
