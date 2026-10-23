# Opens the Windows firewall for hosting a DPT.DOOM game or the master server.
#
#   # what the rules would be, without touching anything
#   powershell -NoProfile -File tools/firewall.ps1 -WhatIf
#
#   # open the game port (the usual case, one command)
#   powershell -NoProfile -File tools/firewall.ps1
#
#   # also open the master port, on the machine that runs the master
#   powershell -NoProfile -File tools/firewall.ps1 -IncludeMaster
#
#   # undo
#   powershell -NoProfile -File tools/firewall.ps1 -Remove
#
# Must run elevated. Without elevation the script prints the exact command to
# re-run and changes nothing, rather than failing halfway through.
#
# Windows XP and Server 2003 have no New-NetFirewallRule, so the script falls
# back to `netsh firewall`. That is not legacy tidiness: XP is a target platform
# for this port, and a firewall script that only works on Windows 10 would leave
# XP users to work it out themselves.

param(
    # Port the game listens on when hosting. Matches NetProtocol.DefaultPort.
    [int]$GamePort = 5029,

    # Port the master listens on. Matches MasterProtocol.DefaultPort.
    [int]$MasterPort = 25135,

    # Also open the master port. Only needed on the machine running the master.
    [switch]$IncludeMaster,

    # Delete the rules instead of adding them.
    [switch]$Remove,

    # Print what would happen and exit.
    [switch]$WhatIf,

    [string]$GameExe = ".\DPTDOOM\bin\Release\net40\dptdoom.exe",
    [string]$MasterExe = ".\DPTDOOM.Master\bin\Release\net40\dptdoom-master.exe"
)

$ErrorActionPreference = "Stop"

$GameRule = "DPT.DOOM game host (TCP $GamePort)"
$MasterRule = "DPT.DOOM master (TCP $MasterPort)"

function Test-Elevated {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)

    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Show-Plan {
    Write-Host "Firewall plan:"
    Write-Host "  game  : TCP $GamePort inbound   (rule '$GameRule')"
    if ($IncludeMaster) {
        Write-Host "  master: TCP $MasterPort inbound (rule '$MasterRule')"
    } else {
        Write-Host "  master: not opened (add -IncludeMaster on the machine running it)"
    }
    Write-Host ""
}

Show-Plan

if ($WhatIf) {
    Write-Host "Nothing was changed (-WhatIf)."
    exit 0
}

if (-not (Test-Elevated)) {
    Write-Host "This needs an elevated PowerShell (Run as administrator)."
    Write-Host ""
    Write-Host "Right-click PowerShell -> 'Run as administrator', then:"
    Write-Host ""

    $reRun = "-NoProfile -ExecutionPolicy Bypass -File `"$($MyInvocation.MyCommand.Path)`" -GamePort $GamePort"
    if ($IncludeMaster) { $reRun += " -IncludeMaster" }
    if ($Remove) { $reRun += " -Remove" }

    Write-Host "  powershell $reRun"
    Write-Host ""
    Write-Host "Or, without the script, in an elevated prompt:"
    Write-Host "  netsh advfirewall firewall add rule name=`"$GameRule`" dir=in action=allow protocol=TCP localport=$GamePort"
    exit 1
}

# The modern path. Windows 8 and later, Server 2012 and later.
$modern = [bool](Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue)

function Add-PortRule([string]$name, [int]$port) {
    if ($modern) {
        # Remove first: adding a rule that already exists is an error, and a
        # script that fails on the second run is worse than no script.
        Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue

        New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow `
            -Protocol TCP -LocalPort $port -Profile Any | Out-Null
    } else {
        # Windows XP / Server 2003. `netsh firewall` predates the advfirewall
        # context and is the only thing available there.
        netsh firewall add portopening TCP $port $name ENABLE | Out-Null
    }

    Write-Host "  ok   $name"
}

function Remove-PortRule([string]$name, [int]$port) {
    if ($modern) {
        $rule = Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue
        if ($rule) {
            $rule | Remove-NetFirewallRule
            Write-Host "  ok   removed $name"
        } else {
            Write-Host "  --   $name was not there"
        }
    } else {
        netsh firewall delete portopening TCP $port | Out-Null
        Write-Host "  ok   removed port $port"
    }
}

if ($Remove) {
    Write-Host "Removing rules:"
    Remove-PortRule $GameRule $GamePort
    if ($IncludeMaster) {
        Remove-PortRule $MasterRule $MasterPort
    }
    Write-Host ""
    Write-Host "Firewall rules removed."
    exit 0
}

Write-Host "Adding rules:"
Add-PortRule $GameRule $GamePort

if ($IncludeMaster) {
    Add-PortRule $MasterRule $MasterPort
}

Write-Host ""
Write-Host "Done. The firewall now allows inbound TCP $GamePort."

if ($IncludeMaster) {
    Write-Host "The master port $MasterPort is open too."
}

Write-Host ""
Write-Host "The firewall is only half of it: a router still has to forward the port"
Write-Host "to this machine, or nobody outside your network can connect. See"
Write-Host "docs/NETWORK.md, section 'Проброс портов'."

# A program rule as well, so a non-default -port still works without editing
# this script. Windows Firewall matches an inbound connection against any
# allowing rule, so this is a fallback, not a duplicate.
#
# The path is resolved relative to the repository, not to the working directory:
# an elevated process started through UAC begins in System32, so a relative
# path silently failed to resolve and the rule was skipped without a word.
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$exePath = Join-Path $root ($GameExe -replace '^\.\\', '')

if (Test-Path $exePath) {
    $exe = (Resolve-Path $exePath).Path
    Write-Host ""

    if ($modern) {
        $programRule = "DPT.DOOM program (any port)"
        Get-NetFirewallRule -DisplayName $programRule -ErrorAction SilentlyContinue |
            Remove-NetFirewallRule -ErrorAction SilentlyContinue

        New-NetFirewallRule -DisplayName $programRule -Direction Inbound -Action Allow `
            -Program $exe -Profile Any | Out-Null
    } else {
        netsh firewall add allowedprogram "$exe" "DPT.DOOM" ENABLE | Out-Null
    }

    Write-Host "  ok   program rule for $exe (covers a custom -port)"
} else {
    Write-Host ""
    Write-Host "  --   no program rule: $exePath not found (build first)"
}
