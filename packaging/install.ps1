# Offline installer shipped inside the release zip.
# Assumes ClaudeLauncher.exe sits next to this script; no .NET and no network needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$launcherDir = Join-Path $HOME '.claude-launcher'
$functionsDir = Join-Path $HOME 'Documents\WindowsPowerShell\functions'
$exeSource = Join-Path $root 'ClaudeLauncher.exe'
$exeTarget = Join-Path $launcherDir 'ClaudeLauncher.exe'

if (-not (Test-Path $exeSource)) {
    throw "ClaudeLauncher.exe was not found next to this script. Extract the whole release zip before running install.cmd."
}

New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
New-Item -ItemType Directory -Path $functionsDir -Force | Out-Null

# Clear Mark-of-the-Web so PowerShell and SmartScreen do not block the files.
Get-ChildItem -Path $root -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# v1.4.x shipped Terminal.Gui and a framework-dependent build; drop the leftovers.
foreach ($stale in @('Terminal.Gui.dll', 'NStack.dll', 'Terminal.Gui.xml', 'NStack.xml',
                     'ClaudeLauncher.dll', 'ClaudeLauncher.deps.json', 'ClaudeLauncher.runtimeconfig.json',
                     'state.json', 'result.json')) {
    Remove-Item (Join-Path $launcherDir $stale) -Force -ErrorAction SilentlyContinue
}

# The exe may be locked by a launcher window that is still open.
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Copy-Item $exeSource $exeTarget -Force
        break
    }
    catch {
        if ($attempt -eq 5) {
            throw "Could not replace $exeTarget - close any running Claude Launcher window and re-run. ($($_.Exception.Message))"
        }

        Write-Host "  $exeTarget is in use, retrying..." -ForegroundColor DarkYellow
        Start-Sleep -Seconds 2
    }
}

Copy-Item (Join-Path $root 'claude-launcher.ps1') (Join-Path $functionsDir 'claude-launcher.ps1') -Force

if (-not (Test-Path $PROFILE)) {
    New-Item -ItemType File -Path $PROFILE -Force | Out-Null
}

$line = '. "$HOME\Documents\WindowsPowerShell\functions\claude-launcher.ps1"'
$content = @(Get-Content $PROFILE -ErrorAction SilentlyContinue)
if ($content -notcontains $line) {
    Add-Content $PROFILE "`r`n$line"
}

Write-Host ''
Write-Host "$(& $exeTarget --version) installed successfully." -ForegroundColor Green
Write-Host ''
Write-Host 'Reload, then run:' -ForegroundColor DarkGray
Write-Host '  . $PROFILE' -ForegroundColor Cyan
Write-Host '  claude-launcher' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Requires: claude on PATH, and a terminal with truecolor (Windows Terminal).' -ForegroundColor DarkGray
Write-Host ''
