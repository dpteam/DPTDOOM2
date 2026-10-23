# Builds the downloadable Windows package.
#
#   powershell -NoProfile -File tools/package-build.ps1 -Version 0.1.0-abc1234
#
# The package contains the shipped net40 builds plus the license and the
# corresponding source, as the GPL requires. It never contains an IWAD, a
# config, a save or a log: those are user data.

param(
    [string]$Version = "dev",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
Push-Location $root

try {
    $staging = Join-Path $root "$Output/dptdoom-$Version"
    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    New-Item -ItemType Directory -Force $staging | Out-Null

    function Copy-Build([string]$project, [string]$target, [string[]]$files) {
        $source = Join-Path $root "$project/bin/$Configuration/net40"
        if (-not (Test-Path $source)) { throw "Missing build output: $source" }

        $destination = Join-Path $staging $target
        New-Item -ItemType Directory -Force $destination | Out-Null

        foreach ($file in $files) {
            $path = Join-Path $source $file
            if (-not (Test-Path $path)) { throw "Missing file: $path" }

            # Only the files named above are copied, so game data, configs,
            # saves and logs that happen to sit next to the build can never end
            # up in a package. This assertion documents that and catches a
            # mistake in the list above.
            if ($file -match '\.(wad|dsg|cfg|log)$') {
                throw "Refusing to package game or user data: $file"
            }

            Copy-Item $path $destination
        }
    }

    Copy-Build "DPTDOOM" "" @("dptdoom.exe", "dptdoom.exe.config")
    Copy-Build "DPTDOOM.Master" "" @("dptdoom-master.exe", "dptdoom-master.exe.config", "DPTDOOM.Shared.dll")
    Copy-Build "DPTDOOM.Launcher" "" @("dptdoom-launcher.exe", "dptdoom-launcher.exe.config", "DPTDOOM.Shared.dll")

    Copy-Item (Join-Path $root "README.md") $staging
    Copy-Item (Join-Path $root "licenses") $staging -Recurse

    # GPL: ship the corresponding source for the exact commit.
    $commit = "unknown"
    try {
        $commit = (git rev-parse HEAD).Trim()
        git archive --format=zip --prefix=source/ --output (Join-Path $staging "source.zip") HEAD
    }
    catch {
        Write-Warning "Could not archive the source; the package will point at the repository instead."
    }

    $note = @"
DPT.DOOM $Version
Commit: $commit
Built from: $(if (Test-Path (Join-Path $staging 'source.zip')) { 'source.zip in this package' } else { 'the repository at the commit above' })

IWAD is not included. Provide your own DOOM2.WAD / DOOM.WAD / FREEDOOM*.WAD
and put it next to dptdoom.exe, or pick it in the launcher.

Run:
  dptdoom.exe -iwad DOOM2.WAD
  dptdoom-launcher.exe
  dptdoom-master.exe --bind 0.0.0.0 --port 25135

Requires .NET Framework 4.0 (included in Windows Vista and later; install it on
Windows XP SP3). No other runtime, no DLLs to download.
"@
    Set-Content (Join-Path $staging "READ-ME-FIRST.txt") $note -Encoding utf8

    $zip = Join-Path $root "$Output/dptdoom-windows-x64-$Version.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $zip,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $size = [Math]::Round((Get-Item $zip).Length / 1MB, 2)
    Write-Host "Created $zip ($size MB)"

    Remove-Item $staging -Recurse -Force
}
finally {
    Pop-Location
}
