<#
.SYNOPSIS
    Builds dist\ScreengrabberSetup-<version>.exe: a per-user installer with the .NET runtime included.

.DESCRIPTION
    1. Publishes Screengrabber as a self-contained 64-bit app (plus its 32-bit email helper) to dist\app.
    2. Compiles installer\Screengrabber.iss with Inno Setup 6 (winget install JRSoftware.InnoSetup).
    The resulting setup runs on any Windows 10 (1809+) / 11 x64 PC without installing anything else.
#>
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "src\Screengrabber\Screengrabber.csproj"
$app = Join-Path $root "dist\app"

$iscc = @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe") |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

$version = ([xml](Get-Content $project)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1

if (Test-Path $app) { Remove-Item $app -Recurse -Force }
dotnet publish $project -c Release -r win-x64 --self-contained true -p:DebugType=none -o $app
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
if (-not (Test-Path (Join-Path $app "Screengrabber.MapiHelper.exe"))) { throw "The MAPI helper wasn't published." }

& $iscc /Qp "/DAppVersion=$version" (Join-Path $root "installer\Screengrabber.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$setup = Join-Path $root "dist\ScreengrabberSetup-$version.exe"
"Built $setup ({0:N1} MB)" -f ((Get-Item $setup).Length / 1MB)
