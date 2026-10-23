# Drives the HTML launcher through its bridge and captures both themes.
#
#   powershell -NoProfile -File tools/capture-showcase.ps1
#
# Output goes to docs/images/. Only the launcher window is captured: no
# desktop, no taskbar, no other windows.

param(
    [string]$Out = "docs/images"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DptCapture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
"@

[void][DptCapture]::SetProcessDPIAware()

$exe = Resolve-Path ".\DPTDOOM.Launcher\bin\Release\net40\dptdoom-launcher.exe"
$asm = [Reflection.Assembly]::LoadFrom($exe)
$form = $asm.CreateInstance("DPTDOOM.Launcher.LauncherForm")
$form.Show()

function Pump([int]$Milliseconds = 200) {
    $deadline = (Get-Date).AddMilliseconds($Milliseconds)
    while ((Get-Date) -lt $deadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
}

Pump 1200

$browser = $form.Controls[0]
$script:page = $null

for ($i = 0; $i -lt 100; $i++) {
    Pump 100
    if ($browser.Document -ne $null) {
        $state = $browser.Document.InvokeScript("eval", @("typeof window.dptReceive"))
        if ($state -eq "function") { break }
    }
}

function Invoke-Page([string]$text) {
    Pump 80
    return $browser.Document.InvokeScript("eval", @($text))
}

function Save-Window([string]$path) {
    Pump 400

    $rect = New-Object DptCapture+RECT
    [void][DptCapture]::GetWindowRect($form.Handle, [ref]$rect)

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bitmap = New-Object System.Drawing.Bitmap $width, $height
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))

    $directory = Split-Path -Parent $path
    if ($directory -and -not (Test-Path $directory)) { New-Item -ItemType Directory -Force $directory | Out-Null }

    $bitmap.Save((Join-Path (Get-Location) $path), [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()

    Write-Host "Saved $path ($width x $height)"
}

# A realistic, obviously fake path so the screenshots carry no personal data.
Invoke-Page "document.getElementById('iwad').value='C:\\DOOM\\DOOM2.WAD';var i=document.getElementById('iwad');i.onchange();" | Out-Null
Invoke-Page "document.getElementById('map').value='7';document.getElementById('skill').value='3';document.getElementById('name').value='DPT';var m=document.getElementById('map');m.onchange();" | Out-Null
Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[0].onclick();" | Out-Null

Invoke-Page "if (document.documentElement.className !== 'dark') document.getElementById('theme-toggle').onclick();" | Out-Null
Pump 500
Save-Window (Join-Path $Out "launcher-dark.png")

Invoke-Page "if (document.documentElement.className === 'dark') document.getElementById('theme-toggle').onclick();" | Out-Null
Pump 500
Save-Window (Join-Path $Out "launcher-light.png")

$form.Close()
Write-Host "Done."
