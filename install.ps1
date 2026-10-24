<#
.SYNOPSIS
    Builds Screengrabber and installs it for the current user (no admin rights needed).

.DESCRIPTION
    Publishes one Screengrabber.exe to .\dist, copies it to %LOCALAPPDATA%\Programs\Screengrabber,
    adds a Start menu shortcut, and starts it. The app registers itself to start with Windows
    (changeable in Settings). Requires the 32-bit (x86) .NET 10 Desktop Runtime.

.PARAMETER Uninstall
    Removes the installed exe, the Start menu shortcut, and the startup entry.
    Screenshots and settings are kept.

.PARAMETER PublishOnly
    Builds .\dist\Screengrabber.exe without installing.
#>
param([switch]$Uninstall, [switch]$PublishOnly)
$ErrorActionPreference = "Stop"

$installDir = Join-Path $env:LOCALAPPDATA "Programs\Screengrabber"
$installedExe = Join-Path $installDir "Screengrabber.exe"
$shortcut = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Screengrabber.lnk"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$dist = Join-Path $PSScriptRoot "dist"

function Stop-Screengrabber {
    # It's a tray app with no main window to close politely; it holds no unsaved state.
    Get-Process Screengrabber -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

if ($Uninstall) {
    Stop-Screengrabber
    Remove-ItemProperty $runKey -Name Screengrabber -ErrorAction SilentlyContinue
    Remove-Item $shortcut -ErrorAction SilentlyContinue
    Remove-Item $installedExe -ErrorAction SilentlyContinue
    Remove-Item $installDir -ErrorAction SilentlyContinue   # only if now empty
    "Uninstalled. Your screenshots and %APPDATA%\Screengrabber\settings.json were kept."
    return
}

$runtimes = & "${env:ProgramFiles(x86)}\dotnet\dotnet.exe" --list-runtimes 2>$null
if (-not ($runtimes -match "Microsoft\.WindowsDesktop\.App 10\.")) {
    throw "The 32-bit .NET 10 Desktop Runtime is missing. Install the x86 'Windows Desktop Runtime' from https://dotnet.microsoft.com/download/dotnet/10.0 and run this again."
}

# Framework-dependent single file: a small exe that uses the shared x86 .NET 10 runtime (and its security updates).
dotnet publish (Join-Path $PSScriptRoot "src\Screengrabber") -c Release -r win-x86 --self-contained false `
    -p:PublishSingleFile=true -p:DebugType=none -o $dist
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
if ($PublishOnly) {
    "Built $dist\Screengrabber.exe"
    return
}

Stop-Screengrabber
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $dist "Screengrabber.exe") $installedExe -Force

$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $installedExe
$link.Description = "Screenshot to anywhere. Opens Settings if already running."
$link.Save()

Start-Process $installedExe
"Installed to $installedExe and started. Look for the icon in the system tray."
