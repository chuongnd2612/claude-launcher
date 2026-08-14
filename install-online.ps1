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

# Invoke-WebRequest's own progress banner is the blue "Writing request stream..."
# block, and on 5.1 it also throttles the transfer badly. Silence it; this script
# draws its own bar instead.
$ProgressPreference = 'SilentlyContinue'

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

# A bar is only useful on a real console; keep piped or redirected output plain.
$script:showBar = $true
try { if ([Console]::IsOutputRedirected) { $script:showBar = $false } } catch { $script:showBar = $false }

$script:barWidth = 24

function Format-Size {
    param([double]$Bytes)

    if ($Bytes -ge 1MB) { return '{0:0.0} MB' -f ($Bytes / 1MB) }
    return '{0:0} KB' -f ($Bytes / 1KB)
}

function Write-DownloadBar {
    param([string]$Label, [long]$Current, [long]$Total, [switch]$Done)

    if (-not $script:showBar) { return }

    # Not $current for a local: PowerShell variable names are case-insensitive,
    # so that would overwrite the $Current parameter and every percentage would
    # read 0.
    $copiedText = Format-Size $Current

    if ($Total -gt 0) {
        $percent = [int][math]::Floor($Current * 100 / $Total)
        if ($percent -gt 100) { $percent = 100 }
        $filled = [int][math]::Floor($script:barWidth * $percent / 100)
        $suffix = '{0,3}%  {1,9} / {2}' -f $percent, $copiedText, (Format-Size $Total)
    }
    else {
        # No Content-Length: show what has arrived and fill the bar at the end.
        $filled = if ($Done) { $script:barWidth } else { 0 }
        $suffix = '{0,9}' -f $copiedText
    }

    Write-Host ("`r  {0} " -f $Label.PadRight(20)) -NoNewline -ForegroundColor DarkGray
    Write-Host '[' -NoNewline -ForegroundColor DarkGray
    Write-Host ('#' * $filled) -NoNewline -ForegroundColor Cyan
    Write-Host ('-' * ($script:barWidth - $filled)) -NoNewline -ForegroundColor DarkGray
    Write-Host ("] {0}   " -f $suffix) -NoNewline -ForegroundColor DarkGray
    if ($Done) { Write-Host '' }
}

function Get-ReleaseFile {
    param([string]$Name, [string]$Destination, [string]$Label)

    $url = "$base/$Name"
    $response = $null
    $stream = $null
    $file = $null

    try {
        # Streamed by hand rather than via Invoke-WebRequest, so the byte count
        # is available to draw progress with.
        $request = [Net.WebRequest]::Create($url)
        $request.UserAgent = 'claude-launcher-installer'
        $request.Timeout = 30000
        $request.ReadWriteTimeout = 120000

        $response = $request.GetResponse()
        $total = $response.ContentLength
        $stream = $response.GetResponseStream()
        $file = [IO.File]::Create($Destination)

        $buffer = New-Object byte[] 131072
        $copied = [long]0
        $clock = [Diagnostics.Stopwatch]::StartNew()

        if ($Label) { Write-DownloadBar -Label $Label -Current 0 -Total $total }

        while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
            $file.Write($buffer, 0, $read)
            $copied += $read

            # Repainting on every chunk costs more than the download does.
            if ($Label -and $clock.ElapsedMilliseconds -ge 80) {
                Write-DownloadBar -Label $Label -Current $copied -Total $total
                $clock.Restart()
            }
        }

        if ($Label) { Write-DownloadBar -Label $Label -Current $copied -Total $total -Done }
    }
    catch {
        if ($Label -and $script:showBar) { Write-Host '' }
        throw "Could not download $Name from $url : $($_.Exception.Message)"
    }
    finally {
        if ($file) { $file.Dispose() }
        if ($stream) { $stream.Dispose() }
        if ($response) { $response.Close() }
    }
}

# Download to a temporary name first: the running exe may be locked.
$pending = "$exePath.new"
Remove-Item $pending -Force -ErrorAction SilentlyContinue
if (-not $script:showBar) { Write-Host '  downloading ClaudeLauncher.exe' -ForegroundColor DarkGray }
Get-ReleaseFile -Name 'ClaudeLauncher.exe' -Destination $pending -Label 'ClaudeLauncher.exe'

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

if (-not $script:showBar) { Write-Host '  downloading claude-launcher.ps1' -ForegroundColor DarkGray }
Get-ReleaseFile -Name 'claude-launcher.ps1' -Destination $wrapperPath -Label 'claude-launcher.ps1'

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
