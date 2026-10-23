# Captures the main window of a running process to a PNG file.
#
#   powershell -NoProfile -File tools/capture-window.ps1 -Process dptdoom-launcher -Out docs/images/launcher.png
#
# Only the window contents are captured: no desktop, no taskbar, no other
# windows, and **no window shadow**. Used for the screenshots in README.md.
#
# Why the shadow needed its own code path: GetWindowRect reports the *extended*
# frame, which on Windows 10 and 11 includes about seven pixels of invisible
# resize border on each side. Capturing the rectangle as reported therefore put a
# thin strip of desktop around the window — visible as a dark edge on screenshots
# with a light background. DwmGetWindowAttribute with DWMWA_EXTENDED_FRAME_BOUNDS
# returns what is actually drawn.

param(
    [Parameter(Mandatory = $true)][string]$Process,
    [Parameter(Mandatory = $true)][string]$Out,
    [int]$WaitSeconds = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;

public static class DptWindow {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();

    // Returns the bounds the compositor actually draws: no shadow, no invisible
    // resize border. Absent before Windows Vista, hence the fallback.
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(
        IntPtr hwnd, int attribute, out RECT value, int size);

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public static bool TryGetVisibleBounds(IntPtr hwnd, out RECT bounds) {
        try {
            if (DwmGetWindowAttribute(hwnd, DWMWA_EXTENDED_FRAME_BOUNDS, out bounds,
                    Marshal.SizeOf(typeof(RECT))) == 0) {
                return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
            }
        } catch (DllNotFoundException) {
            // Windows XP: dwmapi.dll does not exist.
        } catch (EntryPointNotFoundException) {
            // Vista without the compositor.
        }

        return GetWindowRect(hwnd, out bounds);
    }
}
"@

[void][DptWindow]::SetProcessDPIAware()

$proc = Get-Process -Name $Process -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) { throw "Process '$Process' is not running." }

$deadline = (Get-Date).AddSeconds($WaitSeconds)
while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline) {
    Start-Sleep -Milliseconds 250
    $proc.Refresh()
}

if ($proc.MainWindowHandle -eq 0) { throw "Process '$Process' has no main window." }

[void][DptWindow]::SetForegroundWindow($proc.MainWindowHandle)
Start-Sleep -Milliseconds 700

$rect = New-Object DptWindow+RECT
$usedVisibleBounds = [DptWindow]::TryGetVisibleBounds($proc.MainWindowHandle, [ref]$rect)

if (-not $usedVisibleBounds) {
    [void][DptWindow]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)
}

$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
if ($width -le 0 -or $height -le 0) { throw "Bad window rect." }

$bitmap = New-Object System.Drawing.Bitmap $width, $height
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))

$directory = Split-Path -Parent $Out
if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Force $directory | Out-Null }

$bitmap.Save((Join-Path (Get-Location) $Out), [System.Drawing.Imaging.ImageFormat]::Png)
$graphics.Dispose()
$bitmap.Dispose()

$source = if ($usedVisibleBounds) { "visible bounds" } else { "window rect (no DWM)" }
Write-Host "Saved $Out ($width x $height, $source)"
