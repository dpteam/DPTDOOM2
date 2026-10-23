# Captures the main window of a running process to a PNG file.
#
#   powershell -NoProfile -File tools/capture-window.ps1 -Process dptdoom-launcher -Out docs/images/launcher.png
#
# Only the window contents are captured: no desktop, no taskbar, no other
# windows. Used for the screenshots in README.md.

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
[void][DptWindow]::GetWindowRect($proc.MainWindowHandle, [ref]$rect)

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

Write-Host "Saved $Out ($width x $height)"
