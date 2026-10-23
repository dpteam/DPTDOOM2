# Verifies the "no third-party dependencies" promise.
#
#   powershell -NoProfile -File tools/check-standalone.ps1
#
# Two separate things are checked, because either one alone can regress:
#
#   1. The shipping projects must not reference any NuGet package except the
#      .NET Framework reference assemblies, which are a compile-time artifact
#      and never end up in the output.
#   2. The net40 output directories must contain no DLL beyond the project's
#      own assemblies. A single added package would show up here as an extra
#      file next to the executable.

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

# Projects that are part of the shipped product.
$shipping = @(
    "DPTDOOM/DPTDOOM.csproj",
    "DPTDOOM.Shared/DPTDOOM.Shared.csproj",
    "DPTDOOM.Master/DPTDOOM.Master.csproj",
    "DPTDOOM.Launcher/DPTDOOM.Launcher.csproj"
)

$allowedPackages = @("Microsoft.NETFramework.ReferenceAssemblies")

Write-Host "Package references"
foreach ($project in $shipping) {
    $path = Join-Path $root $project
    if (-not (Test-Path $path)) {
        Fail "$project is missing"
        continue
    }

    [xml]$xml = Get-Content $path
    $references = $xml.SelectNodes("//PackageReference")

    $names = @()
    foreach ($reference in $references) {
        $name = $reference.GetAttribute("Include")
        if ($name) { $names += $name }
    }

    $unexpected = $names | Where-Object { $allowedPackages -notcontains $_ }
    if ($unexpected) {
        Fail "$project references $($unexpected -join ', ')"
    } else {
        Ok "$project"
    }
}

# Output directories of the shipped net40 builds.
$outputs = @(
    @{ Project = "DPTDOOM";        Own = @("dptdoom.exe") },
    @{ Project = "DPTDOOM.Master"; Own = @("dptdoom-master.exe", "DPTDOOM.Shared.dll") },
    @{ Project = "DPTDOOM.Launcher"; Own = @("dptdoom-launcher.exe", "DPTDOOM.Shared.dll") }
)

Write-Host ""
Write-Host "net40 output contents"

foreach ($entry in $outputs) {
    $directory = Join-Path $root "$($entry.Project)/bin/$Configuration/net40"

    if (-not (Test-Path $directory)) {
        Fail "$($entry.Project): no net40 build in $directory"
        continue
    }

    $unexpected = @()
    foreach ($file in Get-ChildItem $directory -File) {
        $name = $file.Name

        # Own assemblies, symbols and the framework binding config are fine.
        if ($entry.Own -contains $name) { continue }
        if ($name -like "*.pdb") { continue }
        if ($name -like "*.exe.config") { continue }
        if ($name -like "*.deps.json") { continue }
        if ($name -like "*.runtimeconfig.json") { continue }

        # Runtime data written by running the game from its own folder. Not a
        # build artifact, and absent on a clean build.
        if ($name -like "*.cfg") { continue }
        if ($name -like "*.dsg") { continue }
        if ($name -like "*.log") { continue }
        if ($name -like "*.tsv") { continue }
        if ($name -like "*.bmp") { continue }
        if ($name -like "*.wav") { continue }

        $unexpected += $name
    }

    if ($unexpected.Count -gt 0) {
        Fail "$($entry.Project): unexpected files in the shipped build: $($unexpected -join ', ')"
    } else {
        Ok "$($entry.Project): only its own files"
    }
}

# A DLL next to the game executable would mean a third-party runtime
# dependency slipped in; the game ships as a single exe.
$gameDirectory = Join-Path $root "DPTDOOM/bin/$Configuration/net40"
if (Test-Path $gameDirectory) {
    $dlls = @(Get-ChildItem $gameDirectory -Filter *.dll -File)
    if ($dlls.Count -gt 0) {
        Fail "the game executable has DLLs next to it: $($dlls.Name -join ', ')"
    } else {
        Ok "the game executable is a single file"
    }
}

# Forbidden APIs are not scanned with regexes: the net40 compiler is the
# authority on what exists in .NET Framework 4.0, and a `Span<T>` or
# `Math.Clamp` would fail the build. The regex approach also produced false
# positives on the project's own shims (MathF, TryAdd) and on comments.
#
# What the compiler cannot tell us is which framework the output is actually
# for, so that is checked from the generated runtime config.
Write-Host ""
Write-Host "Target framework"

foreach ($project in $shipping) {
    $path = Join-Path $root $project
    if (-not (Test-Path $path)) { continue }

    [xml]$xml = Get-Content $path
    $frameworks = $xml.SelectSingleNode("//TargetFrameworks")
    if ($null -eq $frameworks) {
        $frameworks = $xml.SelectSingleNode("//TargetFramework")
    }

    $value = if ($frameworks) { $frameworks.InnerText } else { "" }

    if ($value -notmatch "net40") {
        Fail "$project does not target net40 (TargetFrameworks='$value')"
    } else {
        Ok "$project targets net40"
    }
}

$configs = @(
    "DPTDOOM/bin/$Configuration/net40/dptdoom.exe.config",
    "DPTDOOM.Master/bin/$Configuration/net40/dptdoom-master.exe.config",
    "DPTDOOM.Launcher/bin/$Configuration/net40/dptdoom-launcher.exe.config"
)

foreach ($config in $configs) {
    $path = Join-Path $root $config
    if (-not (Test-Path $path)) {
        Fail "$config is missing"
        continue
    }

    $text = Get-Content $path -Raw
    if ($text -notmatch 'sku="\.NETFramework,Version=v4\.0"') {
        Fail "$config does not declare .NET Framework 4.0"
    } else {
        Ok "$config declares .NET Framework 4.0"
    }
}

Write-Host ""
if ($failures -gt 0) {
    throw "$failures standalone check(s) failed."
}

Write-Host "Standalone check passed."
