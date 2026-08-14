# Claude Launcher online installer - no .NET required on this machine.
#
#   irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex
#
# Or pin a version / fork:
#   & ([scriptblock]::Create((irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1))) -Version v1.5.0
#
# This file must stay UTF-8 *without* a BOM, unlike the wrapper. `irm` hands the
# BOM to `iex` as a literal U+FEFF character, which stops param() from being the
# first statement and fails the one-liner with "Unexpected attribute
# 'CmdletBinding'". Keep the body ASCII-only so no BOM is ever needed. ci.yml
# enforces this.
[CmdletBinding()]
param(
    [string]$Repo = 'chuongnd2612/claude-launcher',
    [string]$Version = 'latest',
    [switch]$SkipProfile
)

$ErrorActionPreference = 'Stop'

# Windows PowerShell 5.1 still defaults to TLS 1.0 against github.com.
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
}
catch { }

$launcherDir = Join-Path $HOME '.claude-launcher'
$functionsDir = Join-Path $HOME 'Documents\WindowsPowerShell\functions'
$exePath = Join-Path $launcherDir 'ClaudeLauncher.exe'
$wrapperPath = Join-Path $functionsDir 'claude-launcher.ps1'

New-Item -ItemType Directory -Path $launcherDir -Force | Out-Null
New-Item -ItemType Directory -Path $functionsDir -Force | Out-Null

if ($Version -eq 'latest') {
    $base = "https://github.com/$Repo/releases/latest/download"
    $tagLabel = 'latest'
}
else {
    $base = "https://github.com/$Repo/releases/download/$Version"
    $tagLabel = $Version
}

Write-Host ''
Write-Host "Installing Claude Launcher ($tagLabel) from $Repo" -ForegroundColor Cyan
Write-Host ''

function Get-ReleaseFile {
    param([string]$Name, [string]$Destination)

    $url = "$base/$Name"
    try {
        Invoke-WebRequest -Uri $url -OutFile $Destination -UseBasicParsing
    }
    catch {
        throw "Could not download $Name from $url : $($_.Exception.Message)"
    }
}

# Download to a temporary name first: the running exe may be locked.
$pending = "$exePath.new"
Remove-Item $pending -Force -ErrorAction SilentlyContinue
Write-Host '  downloading ClaudeLauncher.exe' -ForegroundColor DarkGray
Get-ReleaseFile -Name 'ClaudeLauncher.exe' -Destination $pending

# Verify the checksum when the release publishes one.
$checksumFile = "$pending.sha256"
$verified = $false
try {
    Get-ReleaseFile -Name 'ClaudeLauncher.exe.sha256' -Destination $checksumFile
    $expected = ((Get-Content $checksumFile -Raw) -split '\s+')[0].ToLower()
    $actual = (Get-FileHash $pending -Algorithm SHA256).Hash.ToLower()
    if ($expected -and $expected -ne $actual) {
        Remove-Item $pending, $checksumFile -Force -ErrorAction SilentlyContinue
        throw "Checksum mismatch. Expected $expected but downloaded $actual. Aborting."
    }
    $verified = [bool]$expected
}
catch {
    if ($_.Exception.Message -like 'Checksum mismatch*') { throw }
    Write-Host '  no checksum published for this release, skipping verification' -ForegroundColor DarkYellow
}
finally {
    Remove-Item $checksumFile -Force -ErrorAction SilentlyContinue
}

if ($verified) { Write-Host '  checksum verified' -ForegroundColor DarkGray }

# Swap the binary in, retrying while a previous launcher session exits.
for ($attempt = 1; $attempt -le 5; $attempt++) {
    try {
        Move-Item $pending $exePath -Force
        break
    }
    catch {
        if ($attempt -eq 5) {
            throw "Could not replace $exePath - close any running Claude Launcher window and re-run. ($($_.Exception.Message))"
        }

        Write-Host "  $exePath is in use, retrying..." -ForegroundColor DarkYellow
        Start-Sleep -Seconds 2
    }
}

Write-Host '  downloading claude-launcher.ps1' -ForegroundColor DarkGray
Get-ReleaseFile -Name 'claude-launcher.ps1' -Destination $wrapperPath

# Clear Mark-of-the-Web so PowerShell and SmartScreen do not block the files.
Unblock-File $exePath -ErrorAction SilentlyContinue
Unblock-File $wrapperPath -ErrorAction SilentlyContinue

# v1.4.x shipped Terminal.Gui; those DLLs are dead weight now.
foreach ($stale in @('Terminal.Gui.dll', 'NStack.dll', 'Terminal.Gui.xml', 'NStack.xml',
                     'ClaudeLauncher.dll', 'ClaudeLauncher.deps.json', 'ClaudeLauncher.runtimeconfig.json')) {
    Remove-Item (Join-Path $launcherDir $stale) -Force -ErrorAction SilentlyContinue
}
Remove-Item (Join-Path $launcherDir 'state.json') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $launcherDir 'result.json') -Force -ErrorAction SilentlyContinue

if (-not $SkipProfile) {
    if (-not (Test-Path $PROFILE)) {
        New-Item -ItemType File -Path $PROFILE -Force | Out-Null
    }

    $line = '. "$HOME\Documents\WindowsPowerShell\functions\claude-launcher.ps1"'
    $content = @(Get-Content $PROFILE -ErrorAction SilentlyContinue)
    if ($content -notcontains $line) {
        Add-Content $PROFILE "`r`n$line"
        Write-Host '  registered in $PROFILE' -ForegroundColor DarkGray
    }
    else {
        Write-Host '  $PROFILE already registered' -ForegroundColor DarkGray
    }
}

$installed = & $exePath --version

Write-Host ''
Write-Host "$installed installed." -ForegroundColor Green
Write-Host ''
Write-Host 'Reload, then run:' -ForegroundColor DarkGray
Write-Host '  . $PROFILE' -ForegroundColor Cyan
Write-Host '  claude-launcher' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Requires: claude on PATH, and a terminal with truecolor (Windows Terminal).' -ForegroundColor DarkGray
Write-Host ''
