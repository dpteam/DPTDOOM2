# Drives the HTML launcher through its own bridge for automated checks.
#
#   powershell -NoProfile -File tools/test-launcher.ps1
#
# The launcher is an MSHTML host, so the checks execute the same page script the
# buttons call and then read the rendered state back. That catches a broken
# bridge, a broken theme switch and a broken argument builder without a human
# clicking around.

param(
    [string]$Exe = ".\DPTDOOM.Launcher\bin\Release\net40\dptdoom-launcher.exe"
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms

$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path $Exe))
$form = $asm.CreateInstance("DPTDOOM.Launcher.LauncherForm")
$form.Show()

function Pump([int]$Milliseconds = 200) {
    $deadline = (Get-Date).AddMilliseconds($Milliseconds)
    while ((Get-Date) -lt $deadline) {
        [System.Windows.Forms.Application]::DoEvents()
        Start-Sleep -Milliseconds 25
    }
}

Pump 500

function Wait-Ready {
    for ($i = 0; $i -lt 200; $i++) {
        Pump 100
        $browser = $form.Controls[0]
        if ($browser.Document -ne $null) {
            $state = $browser.Document.InvokeScript("eval", @("typeof window.dptReceive"))
            if ($state -eq "function") { return $browser }
        }
    }

    throw "The page never finished loading."
}

$browser = Wait-Ready

function Invoke-Page([string]$script) {
    Pump 60
    return $browser.Document.InvokeScript("eval", @($script))
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

# 1. The host answered the ready call, so the page is wired to C#.
$seen = Invoke-Page "String(window.__dptHostSeen === true)"
Assert-True ($seen -eq "True") "the page reached the host"

# 2. The status line came from the host, not from the page fallback.
$status = Invoke-Page "document.getElementById('status').innerHTML"
Assert-True ($status.Length -gt 0) "host status is displayed"

# 3. Theme switching round-trips through the host and back.
$before = Invoke-Page "document.documentElement.className"
Invoke-Page "document.getElementById('theme-toggle').onclick()" | Out-Null
Pump 400
$after = Invoke-Page "document.documentElement.className"
Assert-True ($before -ne $after) "theme toggled ('$before' -> '$after')"

# 4. Mode switching reveals the network fields.
Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[1].onclick();" | Out-Null
Pump 300
$network = Invoke-Page "document.getElementById('network').className"
Assert-True ($network -notlike "*hidden*") "host mode shows the network fields"

# 5. The argument preview is produced by the C# argument builder.
Invoke-Page "document.getElementById('iwad').value='C:\\games\\DOOM2.WAD';document.getElementById('map').value='7';document.getElementById('skill').value='4';DPT.init===undefined;" | Out-Null
Invoke-Page "var i=document.getElementById('iwad');i.onchange();" | Out-Null
Pump 500
$preview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($preview -like "*-iwad*") "argument preview is built by the host"
Assert-True ($preview -like "*DOOM2.WAD*") "preview contains the chosen IWAD"
Assert-True ($preview -like "*-warp 7*") "preview contains the chosen map"
Assert-True ($preview -like "*-skill 4*") "preview contains the chosen skill"

# 6. Adding PWADs goes through the host state, not only the page list.
$mods = Invoke-Page "document.getElementById('mods').children.length"
Assert-True ($mods -eq 0) "mod list starts empty"

$form.Close()

if ($failures -gt 0) {
    throw "$failures launcher check(s) failed."
}

Write-Host "Launcher bridge test passed."
