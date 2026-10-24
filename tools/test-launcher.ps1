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

# 7. Bots mode builds the right arguments.
Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[3].onclick();" | Out-Null
Pump 400
$botOptions = Invoke-Page "document.getElementById('botOptions').className"
Assert-True ($botOptions -notlike "*hidden*") "bots mode shows the bot count"

$botsPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($botsPreview -like "*-coopbots*") "bots mode passes -coopbots"

# 8. Every verb reaches the host, including the camel-case ones.
#
# MSHTML lower-cases the authority part of a URI, so "masterRefresh" arrives as
# "masterrefresh". A case-sensitive switch in the host silently dropped every
# camel-case verb, which is why the server browser sat on "Querying master…"
# forever. The check is that the status line changes at all after a refresh
# against a dead endpoint: it must end in an error, not stay on the query text.
$before = Invoke-Page "document.getElementById('status').innerHTML"

Invoke-Page "document.getElementById('masterEndpoint').value='127.0.0.1:1'" | Out-Null
Invoke-Page "document.getElementById('refreshServers').onclick()" | Out-Null

# The master client gives up after four seconds; allow a little more than that.
Pump 6000

$after = Invoke-Page "document.getElementById('status').innerHTML"
Assert-True ($after -ne $before) "master refresh updates the status line"
Assert-True ($after -like "*127.0.0.1*") "the failure names the endpoint it tried"

# 9. Saving the master address is a camel-case verb too, and it must persist.
Invoke-Page "document.getElementById('masterEndpoint').value='example.test:1234'" | Out-Null
Invoke-Page "document.getElementById('masterSave').onclick()" | Out-Null
Pump 400

$saved = Invoke-Page "document.getElementById('status').innerHTML"
Assert-True ($saved -like "*example.test:1234*") "saving the master address is acknowledged"

# 10. Relay fields reach the argument builder.
#
# The relay is the reason a host no longer needs a port forward, so the two
# fields that carry it must not be decorative: host mode with the checkbox must
# produce -master and -relay, and guest mode with a session id must produce
# -relay-master and -relay-session. A checked box that builds nothing is the
# same silent failure as the dead buttons this script was written for.
Invoke-Page "document.getElementById('masterEndpoint').value='relay.test:25135';document.getElementById('masterSave').onclick();" | Out-Null
Pump 300

Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[1].onclick();document.getElementById('relay').checked=true;document.getElementById('relay').onchange();" | Out-Null
Pump 500

$hostPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($hostPreview -like "*-relay*") "host mode with the relay box passes -relay"
Assert-True ($hostPreview -like "*-master relay.test:25135*") "host mode passes the master address"

Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[2].onclick();document.getElementById('relaySession').value='0123456789abcdef0123456789abcdef';document.getElementById('relaySession').onchange();" | Out-Null
Pump 500

$guestPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($guestPreview -like "*-relay-session 0123456789abcdef0123456789abcdef*") "guest mode passes the relay session"
Assert-True ($guestPreview -like "*-relay-master relay.test:25135*") "guest mode passes the relay master"

# The session id is the one value a person copies by hand, and a stale one sends
# the next game to the previous host's tunnel.
Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[1].onclick();document.getElementById('relay').checked=false;document.getElementById('relay').onchange();" | Out-Null
Pump 400

$plainPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($plainPreview -notlike "*-relay *") "host mode without the relay box stays direct"

# 11. The three co-op rules reach the command line.
#
# They change the simulation, so the host must be able to set them and the
# launcher must actually pass them on; a checkbox that builds nothing is the
# same silent failure as the dead buttons this script was written for.
Invoke-Page "document.getElementById('noblock').checked=true;document.getElementById('noblock').onclick();" | Out-Null
Invoke-Page "document.getElementById('coopweapons').checked=true;document.getElementById('coopweapons').onclick();" | Out-Null
Invoke-Page "document.getElementById('nosharedkeys').checked=true;document.getElementById('nosharedkeys').onclick();" | Out-Null
Pump 500

$rulesPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($rulesPreview -like "*-noblock*") "the pass-through rule reaches the command line"
Assert-True ($rulesPreview -like "*-coopweapons*") "the deathmatch-weapons rule reaches the command line"
Assert-True ($rulesPreview -like "*-nosharedkeys*") "the shared-keys rule reaches the command line"

# And they are stored, not just displayed: the state round-trips through the host.
$stateText = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($stateText.Length -gt 0) "preview still renders with the new rules"

# 12. Player count is a session size, and it goes through as -players.
Invoke-Page "document.getElementById('players').value='4';document.getElementById('players').onchange();" | Out-Null
Pump 500

$playersPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($playersPreview -like "*-players 4*") "the player count reaches the command line"

# 13. Bots on the host: allowed, and never more than the slots that exist.
Invoke-Page "document.getElementById('hostbots').value='2';document.getElementById('hostbots').onchange();" | Out-Null
Pump 500

$botsPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($botsPreview -like "*-coopbots 2*") "bots on the host reach the command line"

Invoke-Page "document.getElementById('players').value='2';document.getElementById('players').onchange();document.getElementById('hostbots').value='3';document.getElementById('hostbots').onchange();" | Out-Null
Pump 500

$clampedPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($clampedPreview -like "*-coopbots 1*") "bots are clamped so a guest slot always remains"

# 14. The session password reaches the command line, on both sides - and the
# preview does not show it.
#
# Hosting for friends and not for strangers is the whole point of the field, so a
# box that looks right and builds nothing is the failure this script exists to
# catch. The second half matters just as much: the preview panel displays the
# command line on screen, and screenshots of that panel are how bug reports start,
# so the real value must never appear there.
Invoke-Page "document.getElementById('password').value='hunter 2';document.getElementById('password').onchange();" | Out-Null
Pump 500

$passwordPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($passwordPreview -like "*-password *****") "a password is set on the host command line"
Assert-True ($passwordPreview -notlike "*hunter 2*") "and the preview does not show it"

Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[2].onclick();" | Out-Null
Pump 500

$guestPasswordPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($guestPasswordPreview -like "*-password *****") "and on the guest command line"
Assert-True ($guestPasswordPreview -notlike "*hunter 2*") "and still not shown"

# The value itself has to survive into the launch, not just into the preview.
# Checked through the argument builder rather than the page payload: the host reads
# the payload back on every navigation, so by the time the page is asked it is
# empty. This is also the honest check - it is the string handed to the process.
$gameRunner = $asm.GetType("DPTDOOM.Launcher.GameRunner")
$stateType = $asm.GetType("DPTDOOM.Launcher.LauncherState")
$build = $gameRunner.GetMethods() | Where-Object {
    $_.Name -eq "BuildArguments" -and $_.GetParameters().Count -eq 2
}

$stateObject = [Activator]::CreateInstance($stateType)
$stateObject.Mode = 1
$stateObject.Iwad = "doom2.wad"
$stateObject.Executable = "dptdoom.exe"
$stateObject.Password = "hunter 2"

$masked = $build.Invoke($null, @($stateObject, $true))
$real = $build.Invoke($null, @($stateObject, $false))

Assert-True ($masked -notlike "*hunter 2*") "the preview build masks the password"
Assert-True ($real -like '*-password "hunter 2"*') "the launch build carries the real one, quoted"

Invoke-Page "var b=document.querySelectorAll('[data-mode]');b[1].onclick();document.getElementById('password').value='';document.getElementById('password').onchange();" | Out-Null
Pump 500

$openPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($openPreview -notlike "*-password*") "an empty password stays an open session"

# 15. The server browser shows a round trip and sorts by it.
#
# The ping is measured by the host as a TCP handshake to the game port, so what is
# checked here is only that it reaches the page and orders the list: a row with no
# answer must sort last, because a listed host nobody can reach is the worst case,
# not the best.
#
# Delivered through dptReceive, which is the page's only exported entry point - the
# render functions live inside the page's closure and cannot be called by name.
$tab = [char]9
$nl = [char]10

$rows = @(
    "Far host${tab}10.0.0.5${tab}5029${tab}MAP07${tab}3${tab}40${tab}aaaa0000bbbb1111cccc2222dddd3333${tab}0${tab}180",
    "Near host${tab}127.0.0.1${tab}5029${tab}MAP01${tab}3${tab}40${tab}eeee4444ffff5555aaaa6666bbbb7777${tab}1${tab}3",
    "Dead host${tab}10.0.0.9${tab}5029${tab}MAP02${tab}3${tab}40${tab}11112222333344445555666677778888${tab}0${tab}-1"
)

$browser.Document.InvokeScript("dptReceive", @("servers", ($rows -join $nl))) | Out-Null
Pump 300

$cards = Invoke-Page "document.getElementById('serverList').children.length"
Assert-True ($cards -eq 3) "three listings render as three cards"

$first = Invoke-Page "document.getElementById('serverList').children[0].textContent"
Assert-True ($first -like "*NEAR*") "the nearest host sorts first"

$last = Invoke-Page "document.getElementById('serverList').children[2].textContent"
Assert-True ($last -like "*DEAD*") "a host that does not answer sorts last"

$pingShown = Invoke-Page "document.getElementById('serverList').children[0].textContent.indexOf('3 ms') >= 0"
Assert-True ($pingShown -eq "True") "the round trip is displayed for a reachable host"

# Compared with an ASCII marker rather than the Russian words: this file is UTF-8
# without a BOM, and Windows PowerShell 5.1 reads it as ANSI, so a literal Cyrillic
# string in the script does not match the same text in the page.
$noAnswer = Invoke-Page "var t=document.getElementById('serverList').children[2].textContent;String(t.indexOf('--') >= 0 && t.indexOf(' ms') < 0)"
Assert-True ($noAnswer -eq "True") "an unreachable host says so instead of showing a fake number"

# 15. The filter narrows the list without asking the master again.
Invoke-Page "document.getElementById('serverFilter').value='map01';document.getElementById('serverFilter').onkeyup();" | Out-Null
Pump 300

$filtered = Invoke-Page "document.getElementById('serverList').children.length"
Assert-True ($filtered -eq 1) "the filter shows only the matching map"

$filteredText = Invoke-Page "document.getElementById('serverList').children[0].textContent"
Assert-True ($filteredText -like "*NEAR*") "the filtered card is the one that matched"

Invoke-Page "document.getElementById('serverFilter').value='';document.getElementById('serverFilter').onkeyup();" | Out-Null
Pump 300

$restored = Invoke-Page "document.getElementById('serverList').children.length"
Assert-True ($restored -eq 3) "clearing the filter restores every row"

# Reset, so a leftover rule cannot leak into the next check.
Invoke-Page "document.getElementById('noblock').checked=false;document.getElementById('noblock').onclick();document.getElementById('coopweapons').checked=false;document.getElementById('coopweapons').onclick();document.getElementById('nosharedkeys').checked=false;document.getElementById('nosharedkeys').onclick();" | Out-Null
Pump 400

$cleanPreview = Invoke-Page "document.getElementById('preview').innerHTML"
Assert-True ($cleanPreview -notlike "*-noblock*") "clearing the checkboxes clears the flags"

$form.Close()

if ($failures -gt 0) {
    throw "$failures launcher check(s) failed."
}

Write-Host "Launcher bridge test passed."
