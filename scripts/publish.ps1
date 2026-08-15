# Publish ImageStacker App and Cli as self-contained win-x64 builds into dist/.
# Run from the repository root: .\scripts\publish.ps1

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
Set-Location $Root

$Config = "Release"
$Rid = "win-x64"
$AppOut = Join-Path $Root "dist\app"
$CliOut = Join-Path $Root "dist\cli"

Write-Host "Publishing ImageStacker.App -> $AppOut"
dotnet publish "src\ImageStacker.App" -c $Config -r $Rid --self-contained true -o $AppOut

Write-Host "Publishing ImageStacker.Cli -> $CliOut"
dotnet publish "src\ImageStacker.Cli" -c $Config -r $Rid --self-contained true -o $CliOut

$Notice = @"
Image Stacker — third-party notices
===================================

This distribution includes libvips, used via the NetVips NuGet packages
(NetVips, NetVips.Native.win-x64).

libvips is licensed under the GNU Lesser General Public License (LGPL).
Upstream: https://www.libvips.org/
Source: https://github.com/libvips/libvips

NetVips .NET bindings: https://github.com/kleisauke/net-vips
"@

foreach ($Dir in @($AppOut, $CliOut)) {
    $Notice | Set-Content -Path (Join-Path $Dir "THIRD_PARTY_NOTICES.txt") -Encoding UTF8
}

# Copy any license files shipped with NetVips.Native if present in the NuGet cache.
$nativePkg = Get-ChildItem -Path "$env:USERPROFILE\.nuget\packages\netvips.native.win-x64" -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    Select-Object -First 1
if ($nativePkg) {
    $licenseFiles = Get-ChildItem -Path $nativePkg.FullName -Recurse -Include "LICENSE*","COPYING*","NOTICE*" -File -ErrorAction SilentlyContinue
    foreach ($Dir in @($AppOut, $CliOut)) {
        foreach ($lf in $licenseFiles) {
            Copy-Item -Path $lf.FullName -Destination (Join-Path $Dir $lf.Name) -Force
        }
    }
}

Write-Host "Done. App: $AppOut  Cli: $CliOut"
