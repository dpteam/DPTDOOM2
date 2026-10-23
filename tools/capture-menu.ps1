# Captures the game window with the debug overlay and an open menu.
#
#   powershell -NoProfile -File tools/capture-menu.ps1

param(
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [string]$Iwad = "refs\iwads\doom2.wad",
    [string]$Out = "docs/images"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Iwad)) { throw "IWAD not found: $Iwad" }

Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DptCap {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "PostMessageW")]
    public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

[void][DptCap]::SetProcessDPIAware()

function Find-Window([int]$processId) {
    $script:found = [IntPtr]::Zero
    $cb = [DptCap+EnumProc] {
        param($h, $l)
        $owner = 0
        [void][DptCap]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -ne $processId) { return $true }
        $name = New-Object System.Text.StringBuilder 256
        [void][DptCap]::GetClassNameW($h, $name, 256)
        if ($name.ToString() -eq "DPTDOOMWindow") { $script:found = $h; return $false }
        return $true
    }
    [void][DptCap]::EnumWindows($cb, [IntPtr]::Zero)
    return $script:found
}

function Save-Window([IntPtr]$window, [string]$path) {
    $rect = New-Object DptCap+RECT
    [void][DptCap]::GetWindowRect($window, [ref]$rect)

    $width = $rect.R - $rect.L
    $height = $rect.B - $rect.T

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.L, $rect.T, 0, 0, (New-Object System.Drawing.Size $width, $height))

    $directory = Split-Path -Parent $path
    if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Force $directory | Out-Null }

    $bitmap.Save((Join-Path (Get-Location) $path), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    Write-Host "Saved $path ($width x $height)"
}

$process = Start-Process -FilePath $Exe -ArgumentList '-iwad', $Iwad, '-warp', '1', '-skill', '3' -PassThru

try {
    $window = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $window -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 400
        $window = Find-Window $process.Id
    }

    if ($window -eq [IntPtr]::Zero) { throw "The game window never appeared." }
    Start-Sleep -Seconds 3

    [void][DptCap]::SetForegroundWindow($window)
    Start-Sleep -Milliseconds 500

    # View > FPS counter (1109) turns the overlay on.
    [void][DptCap]::PostMessage($window, 0x0111, [IntPtr]1109, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 800

    Save-Window $window (Join-Path $Out "game-overlay.png")
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
}
