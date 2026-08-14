# Claude Launcher installer for Windows PowerShell 5.1+.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# Remove Mark-of-the-Web from files we install.
Get-ChildItem -Path $root -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

$launcherDir = Join-Path $HOME '.claude-launcher'
# State/result are runtime files; do not carry stale selections across launches.
Remove-Item (Join-Path $launcherDir 'state.json') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $launcherDir 'result.json') -Force -ErrorAction SilentlyContinue
$functionsDir = Join-Path $HOME 'Documents\WindowsPowerShell\functions'
$targetScript = Join-Path $functionsDir 'claude-launcher.ps1'

New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
New-Item -ItemType Directory -Path $functionsDir -Force | Out-Null

# v1.5.0 dropped Terminal.Gui: clear the DLLs left behind by v1.4.x.
foreach ($stale in @('Terminal.Gui.dll', 'NStack.dll', 'Terminal.Gui.xml', 'NStack.xml')) {
    Remove-Item (Join-Path $launcherDir $stale) -Force -ErrorAction SilentlyContinue
}

# Build the native TUI. Requires .NET 8 SDK. No NuGet packages are needed.
# If a prebuilt ClaudeLauncher.exe sits next to this script (release zip), use it
# and skip the SDK requirement entirely.
$prebuilt = Join-Path $root 'ClaudeLauncher.exe'
if (Test-Path $prebuilt) {
    Write-Host 'Using the prebuilt ClaudeLauncher.exe from this package.' -ForegroundColor DarkGray
    foreach ($stale in @('ClaudeLauncher.dll', 'ClaudeLauncher.deps.json', 'ClaudeLauncher.runtimeconfig.json')) {
        Remove-Item (Join-Path $launcherDir $stale) -Force -ErrorAction SilentlyContinue
    }

    Copy-Item $prebuilt $launcherDir -Force
}
else {
    $project = Join-Path $root 'src\ClaudeLauncher.csproj'
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw 'dotnet SDK was not found. Install .NET 8 SDK, or download the prebuilt release from GitHub, then run install.cmd again.'
    }

    dotnet publish $project -c Release -r win-x64 --self-contained false -o $launcherDir --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Claude Launcher build failed (dotnet exit code $LASTEXITCODE). Nothing was registered in the PowerShell profile."
    }
}

Copy-Item (Join-Path $root 'scripts\claude-launcher.ps1') $targetScript -Force
Unblock-File $targetScript -ErrorAction SilentlyContinue

if (-not (Test-Path $PROFILE)) {
    New-Item -ItemType File -Path $PROFILE -Force | Out-Null
}

$line = '. "$HOME\Documents\WindowsPowerShell\functions\claude-launcher.ps1"'
$content = @(Get-Content $PROFILE -ErrorAction SilentlyContinue)
if ($content -notcontains $line) {
    Add-Content $PROFILE "`r`n$line"
}

Write-Host ''
Write-Host "$(& (Join-Path $launcherDir 'ClaudeLauncher.exe') --version) installed successfully." -ForegroundColor Green
Write-Host ''
Write-Host 'Reload:' -ForegroundColor DarkGray
Write-Host '  . $PROFILE' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Run:' -ForegroundColor DarkGray
Write-Host '  claude-launcher' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Tip: the new UI uses 24-bit colors - Windows Terminal is recommended.' -ForegroundColor DarkGray
Write-Host ''
