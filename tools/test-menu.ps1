# Exercises the window menu and the tray icon of the running game.
#
#   powershell -NoProfile -File tools/test-menu.ps1 [-Iwad refs/iwads/doom2.wad]
#
# A menu that builds but does nothing is worse than no menu, so this drives the
# commands the same way a click does: by posting WM_COMMAND to the window. It
# checks that the command actually changes state, not merely that the item
# exists.

param(
    [string]$Exe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [string]$Iwad = "refs\iwads\doom2.wad"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $Iwad)) {
    Write-Host "Skipped: no IWAD at $Iwad."
    exit 0
}

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class DptMenuTest {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetMenu(IntPtr h);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern int GetMenuItemCount(IntPtr menu);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern IntPtr GetSubMenu(IntPtr menu, int pos);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetMenuStringW")]
    public static extern int GetMenuString(IntPtr menu, uint item, StringBuilder text, int max, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "PostMessageW")]
    public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", ExactSpelling = true)]
    public static extern bool GetClientRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$WM_COMMAND = 0x0111

function Find-GameWindow([int]$processId) {
    $script:found = [IntPtr]::Zero
    $callback = [DptMenuTest+EnumProc] {
        param($h, $l)

        $owner = 0
        [void][DptMenuTest]::GetWindowThreadProcessId($h, [ref]$owner)
        if ($owner -ne $processId) { return $true }

        $className = New-Object System.Text.StringBuilder 256
        [void][DptMenuTest]::GetClassNameW($h, $className, 256)

        if ($className.ToString() -eq "DPTDOOMWindow") {
            $script:found = $h
            return $false
        }

        return $true
    }

    [void][DptMenuTest]::EnumWindows($callback, [IntPtr]::Zero)
    return $script:found
}

function Send-Command([IntPtr]$window, [int]$id) {
    [void][DptMenuTest]::PostMessage($window, $WM_COMMAND, [IntPtr]$id, [IntPtr]::Zero)
    Start-Sleep -Milliseconds 350
}

$failures = 0

function Assert-True([bool]$condition, [string]$message) {
    if ($condition) {
        Write-Host "  ok   $message"
    } else {
        Write-Host "  FAIL $message"
        $script:failures++
    }
}

$process = Start-Process -FilePath $Exe -ArgumentList '-iwad', $Iwad, '-warp', '1' -PassThru

try {
    $window = [IntPtr]::Zero
    for ($i = 0; $i -lt 40 -and $window -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 400
        $window = Find-GameWindow $process.Id
    }

    if ($window -eq [IntPtr]::Zero) { throw "The game window never appeared." }
    Start-Sleep -Seconds 2

    Write-Host "Menu bar"
    $menu = [DptMenuTest]::GetMenu($window)
    Assert-True ($menu -ne [IntPtr]::Zero) "the window has a menu bar"

    $count = [DptMenuTest]::GetMenuItemCount($menu)
    Assert-True ($count -ge 5) "menu bar has $count top level items"

    $titles = @()
    for ($i = 0; $i -lt $count; $i++) {
        $text = New-Object System.Text.StringBuilder 64
        [void][DptMenuTest]::GetMenuString($menu, $i, $text, 64, 0x0400)
        $titles += $text.ToString()
    }

    Write-Host "       items: $($titles -join ' | ')"

    # The submenu that used to be appended to the wrong menu.
    $view = [DptMenuTest]::GetSubMenu($menu, 1)
    Assert-True ($view -ne [IntPtr]::Zero) "the View submenu exists"

    $viewCount = [DptMenuTest]::GetMenuItemCount($view)
    $hasSizes = $false
    for ($i = 0; $i -lt $viewCount; $i++) {
        $sub = [DptMenuTest]::GetSubMenu($view, $i)
        if ($sub -ne [IntPtr]::Zero -and [DptMenuTest]::GetMenuItemCount($sub) -eq 4) {
            $hasSizes = $true
        }
    }

    Assert-True $hasSizes "window sizes live in a submenu of View"

    Write-Host ""
    Write-Host "Window size"

    $rect = New-Object DptMenuTest+RECT
    [void][DptMenuTest]::GetClientRect($window, [ref]$rect)
    $startWidth = $rect.R - $rect.L
    Write-Host "       client starts at ${startWidth}px"

    # ViewSize1280 is 1105: ViewFullscreen is 1101, then the four sizes.
    Send-Command $window 1105
    [void][DptMenuTest]::GetClientRect($window, [ref]$rect)
    $wide = $rect.R - $rect.L
    Assert-True ($wide -eq 1280) "command resized the client to $wide"

    # ViewSize640 is 1103.
    Send-Command $window 1103
    [void][DptMenuTest]::GetClientRect($window, [ref]$rect)
    $back = $rect.R - $rect.L
    Assert-True ($back -eq 640) "command resized the client back to $back"

    Write-Host ""
    Write-Host "Commands that must not crash"

    foreach ($id in @(1301, 1302, 1303, 1106, 1107, 1201, 1202, 1203, 1204)) {
        Send-Command $window $id
    }

    $process.Refresh()
    Assert-True (-not $process.HasExited) "the game survived debug and audio commands"

    Write-Host ""
    Write-Host "Full screen"

    Send-Command $window 1101
    [void][DptMenuTest]::GetClientRect($window, [ref]$rect)
    $fullWidth = $rect.R - $rect.L
    Assert-True ($fullWidth -ne 640) "full screen changed the client size ($fullWidth)"

    Send-Command $window 1101
    [void][DptMenuTest]::GetClientRect($window, [ref]$rect)
    Assert-True (($rect.R - $rect.L) -eq 640) "leaving full screen restored the window"

    Write-Host ""
    Write-Host "Tray"

    Send-Command $window 1502
    Assert-True (-not [DptMenuTest]::IsWindowVisible($window)) "TrayHide hid the window"

    Send-Command $window 1501
    Assert-True ([DptMenuTest]::IsWindowVisible($window)) "TrayShow brought it back"

    Write-Host ""
    Write-Host "Overlay"

    Send-Command $window 1109
    $process.Refresh()
    Assert-True (-not $process.HasExited) "the frame counter toggle did not crash"
}
finally {
    if (-not $process.HasExited) {
        $process.Kill()
        $process.WaitForExit(5000) | Out-Null
    }
}

if ($failures -gt 0) {
    throw "$failures menu check(s) failed."
}

Write-Host "Menu test passed."
