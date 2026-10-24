# Checks that the shipped binaries can actually run on Windows XP.
#
#   powershell -NoProfile -File tools/check-xp.ps1
#
# Windows XP is a stated target of this port and, until now, "it still works on XP"
# was an intention rather than a check. Two things can silently break it, and both
# are invisible on a modern machine:
#
#   1. The PE header. If the linker stamps a minimum OS or subsystem version above
#      5.01, Windows XP refuses to load the executable at all - no message, just
#      "not a valid Win32 application". This is a fact in the binary, so it is read
#      straight out of the file rather than trusted.
#
#   2. A Win32 function that did not exist yet. P/Invoke resolves lazily: the code
#      builds, runs and passes tests on Windows 11, then throws
#      EntryPointNotFoundException on XP. Such a call is acceptable only when the
#      call site catches that exception and carries on - which is how DPI awareness
#      is handled, since it did not exist before Vista.
#
# Deliberately not checked: whether every API in the allow list exists on XP. That
# needs the XP-era import tables, which are not available here, and pretending to
# check it would be worse than saying so. What is checked is the fact (the header)
# and the known-risky names.

param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$failures = 0

function Fail([string]$message) {
    Write-Host "  FAIL $message"
    $script:failures++
}

function Ok([string]$message) {
    Write-Host "  ok   $message"
}

# DLLs that exist on Windows XP SP3. Anything else is a hard stop: the loader fails
# before a single line of our code runs.
$xpDlls = @(
    "kernel32.dll", "user32.dll", "gdi32.dll", "winmm.dll", "shell32.dll",
    "advapi32.dll", "ole32.dll", "comdlg32.dll", "comctl32.dll", "version.dll"
)

# Functions that are known to be newer than Windows XP. Calling one is fine only
# when the call site expects it to be missing.
$postXpApis = @(
    "SetProcessDPIAware",
    "SetProcessDpiAwarenessContext",
    "GetDpiForWindow",
    "GetDpiForSystem",
    "GetSystemMetricsForDpi",
    "AdjustWindowRectExForDpi",
    "DwmGetWindowAttribute",
    "DwmExtendFrameIntoClientArea",
    "DwmSetWindowAttribute",
    "AddDllDirectory",
    "SetDefaultDllDirectories",
    "InitializeCriticalSectionEx",
    "GetTickCount64",
    "QueryUnbiasedInterruptTime",
    "SetThreadDescription",
    "GetThreadDescription"
)

# Public assemblies that do not exist in .NET Framework 4.0, or that are not part of
# the framework at all. A reference to one of these means the build cannot load on
# XP no matter what the header says.
$forbiddenReferences = @(
    "WindowsBase", "PresentationCore", "PresentationFramework",
    "System.Runtime", "System.Threading.Tasks.Extensions",
    "Microsoft.Extensions", "Newtonsoft", "System.Text.Json"
)

$binaries = @(
    "DPTDOOM/bin/$Configuration/net40/dptdoom.exe",
    "DPTDOOM.Master/bin/$Configuration/net40/dptdoom-master.exe",
    "DPTDOOM.Launcher/bin/$Configuration/net40/dptdoom-launcher.exe",
    "DPTDOOM.Shared/bin/$Configuration/net40/DPTDOOM.Shared.dll"
)

Write-Host "PE headers"
Write-Host ""

# Windows XP is 5.01. The loader compares the subsystem version, not the OS
# version, but both are stamped by the linker and both are worth knowing.
$xpMajor = 5
$xpMinor = 1

foreach ($binary in $binaries) {
    $path = Join-Path $root $binary

    if (-not (Test-Path $path)) {
        Fail "$binary is missing (build first)"
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($path)

    if ($bytes.Length -lt 0x40) {
        Fail "$binary is too small to be a PE file"
        continue
    }

    $peOffset = [BitConverter]::ToInt32($bytes, 0x3C)

    if ($peOffset -le 0 -or $peOffset + 24 -ge $bytes.Length) {
        Fail "$binary has no PE header"
        continue
    }

    $signature = [BitConverter]::ToUInt32($bytes, $peOffset)

    if ($signature -ne 0x00004550) {
        Fail "$binary has no PE signature"
        continue
    }

    $optional = $peOffset + 24
    $osMajor = [BitConverter]::ToUInt16($bytes, $optional + 40)
    $osMinor = [BitConverter]::ToUInt16($bytes, $optional + 42)
    $subMajor = [BitConverter]::ToUInt16($bytes, $optional + 48)
    $subMinor = [BitConverter]::ToUInt16($bytes, $optional + 50)

    $osTooNew = ($osMajor -gt $xpMajor) -or ($osMajor -eq $xpMajor -and $osMinor -gt $xpMinor)
    $subTooNew = ($subMajor -gt $xpMajor) -or ($subMajor -eq $xpMajor -and $subMinor -gt $xpMinor)

    if ($osTooNew -or $subTooNew) {
        Fail ("{0}: header asks for OS {1}.{2} / subsystem {3}.{4}, above XP's 5.01 - " -f
            $binary, $osMajor, $osMinor, $subMajor, $subMinor) +
            "Windows XP will refuse to load it"
    } else {
        Ok ("{0}: OS {1}.{2}, subsystem {3}.{4}" -f $binary, $osMajor, $osMinor, $subMajor, $subMinor)
    }
}

Write-Host ""
Write-Host "Imported DLLs"

$sources = @()
foreach ($project in @("DPTDOOM", "DPTDOOM.Launcher", "DPTDOOM.Master", "DPTDOOM.Shared")) {
    $directory = Join-Path $root $project
    if (Test-Path $directory) {
        $sources += Get-ChildItem $directory -Recurse -Filter *.cs
    }
}

if ($sources.Count -eq 0) {
    Fail "no sources found to scan"
}

$dllPattern = 'DllImport\("([A-Za-z0-9_]+\.dll)"'
$imported = @{}

foreach ($file in $sources) {
    $text = Get-Content $file.FullName -Raw
    foreach ($match in [regex]::Matches($text, $dllPattern, 'IgnoreCase')) {
        $imported[$match.Groups[1].Value.ToLowerInvariant()] = $true
    }
}

foreach ($dll in $imported.Keys) {
    if ($xpDlls -notcontains $dll) {
        Fail "$dll is not part of Windows XP"
    }
}

if ($imported.Count -gt 0) {
    Ok ("imported DLLs are XP-era: " + (($imported.Keys | Sort-Object) -join ", "))
}

Write-Host ""
Write-Host "Post-XP APIs"

# A call to a newer function is only acceptable when the caller handles its absence.
# The check is textual and deliberately narrow: the guard has to be a catch for
# EntryPointNotFoundException or DllNotFoundException within the same file. That is
# enough to catch the real mistake - adding a Vista-only call to a hot path and
# never noticing - without pretending to be a type system.
$guardedCount = 0
$foundCount = 0

foreach ($file in $sources) {
    $text = Get-Content $file.FullName -Raw
    $lines = Get-Content $file.FullName

    $guarded = $text -match 'catch\s*\(\s*EntryPointNotFoundException' -or
               $text -match 'catch\s*\(\s*DllNotFoundException'

    foreach ($line in $lines) {
        # A declaration is not a call. Skipping these lines is what keeps
        # "public static extern ulong GetTickCount64();" from being reported as a
        # call site - the first version of this check reported both, which made the
        # output look like three problems when there was one.
        if ($line -match '\bextern\b') {
            continue
        }

        foreach ($api in $postXpApis) {
            if ($line -notmatch ('\b' + [regex]::Escape($api) + '\s*\(')) {
                continue
            }

            $foundCount++

            if ($guarded) {
                $guardedCount++
            } else {
                Fail ("{0}: calls {1} with no EntryPointNotFoundException guard - it throws on XP" -f
                    $file.Name, $api)
            }
        }
    }
}

if ($foundCount -eq 0) {
    Ok "no post-XP API is called at all"
} elseif ($foundCount -eq $guardedCount) {
    Ok ("{0} post-XP call site(s), all guarded" -f $foundCount)
}

Write-Host ""
Write-Host "Assembly references"

# The net40 build must not reference anything outside .NET Framework 4.0. That is
# already partly checked by check-standalone.ps1 (packages, stray DLLs); this is the
# other half - a framework type that simply does not exist in 4.0.
foreach ($binary in $binaries) {
    $path = Join-Path $root $binary

    if (-not (Test-Path $path)) {
        continue
    }

    $bytes = [System.IO.File]::ReadAllBytes($path)

    $bad = @()

    foreach ($name in $forbiddenReferences) {
        # An assembly reference is stored as a null-terminated name in the metadata
        # heap, so the terminator matters: "System.Runtime" is a substring of
        # "System.Runtime.InteropServices", which has been in the framework since
        # 2.0. Matching the bare name alone reported every binary as broken.
        $pattern = [System.Text.Encoding]::ASCII.GetBytes($name + [char]0)
        $found = $false

        for ($offset = 0; $offset -le $bytes.Length - $pattern.Length; $offset++) {
            if ($bytes[$offset] -ne $pattern[0]) {
                continue
            }

            $match = $true

            for ($i = 1; $i -lt $pattern.Length; $i++) {
                if ($bytes[$offset + $i] -ne $pattern[$i]) {
                    $match = $false
                    break
                }
            }

            if ($match) {
                $found = $true
                break
            }
        }

        if ($found) {
            $bad += $name
        }
    }

    if ($bad.Count -gt 0) {
        Fail ("{0} references {1}, which is not in .NET Framework 4.0" -f $binary, ($bad -join ", "))
    } else {
        Ok "$binary references no post-4.0 assembly"
    }
}

Write-Host ""

if ($failures -gt 0) {
    throw "$failures XP compatibility check(s) failed."
}

Write-Host "XP compatibility check passed."
