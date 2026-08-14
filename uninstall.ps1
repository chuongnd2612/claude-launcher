# Claude Launcher uninstaller.
#
#   .\uninstall.ps1                       remove the launcher, back up profiles.json + ui.json
#   .\uninstall.ps1 -KeepConfig           leave profiles.json + ui.json in place for a reinstall
#   .\uninstall.ps1 -Purge                remove ~/.claude-launcher entirely, no backup
#   .\uninstall.ps1 -IncludeClaudeConfigDirs   also delete the per-profile Claude data (asks first)
#   .\uninstall.ps1 -WhatIf               show what would happen, change nothing
#
# What is NOT touched unless you ask:
#   - $HOME\.claude-work, $HOME\.claude-personal and friends. Those are your real
#     CLAUDE_CONFIG_DIR folders: conversation history, settings, MCP servers. The
#     launcher only points at them, it does not own them.
#   - Claude Code itself.
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [switch]$KeepConfig,
    [switch]$Purge,
    [switch]$IncludeClaudeConfigDirs,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$launcherDir = Join-Path $HOME '.claude-launcher'
$profilesFile = Join-Path $launcherDir 'profiles.json'
$settingsFile = Join-Path $launcherDir 'ui.json'
$exePath = Join-Path $launcherDir 'ClaudeLauncher.exe'

$profileScripts = @(
    (Join-Path $HOME 'Documents\WindowsPowerShell\Microsoft.PowerShell_profile.ps1'),
    (Join-Path $HOME 'Documents\WindowsPowerShell\profile.ps1'),
    (Join-Path $HOME 'Documents\PowerShell\Microsoft.PowerShell_profile.ps1'),
    (Join-Path $HOME 'Documents\PowerShell\profile.ps1')
)

$wrapperScripts = @(
    (Join-Path $HOME 'Documents\WindowsPowerShell\functions\claude-launcher.ps1'),
    (Join-Path $HOME 'Documents\PowerShell\functions\claude-launcher.ps1')
)

$removed = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Write-Step {
    param([string]$Message)
    Write-Host "  $Message" -ForegroundColor DarkGray
}

function Confirm-Destructive {
    param([string]$Question, [string]$Expected = 'yes')

    if ($Force) { return $true }

    Write-Host ''
    Write-Host $Question -ForegroundColor Yellow
    $answer = Read-Host "Type '$Expected' to confirm"
    return ($answer -eq $Expected)
}

Write-Host ''
Write-Host 'Claude Launcher uninstaller' -ForegroundColor Cyan
Write-Host ''

if ($Purge -and $KeepConfig) {
    throw '-Purge and -KeepConfig contradict each other. Pick one.'
}

# ---------------------------------------------------------------- discover
$installedAnything = (Test-Path $launcherDir) -or ($wrapperScripts | Where-Object { Test-Path $_ })
if (-not $installedAnything) {
    Write-Host '  Nothing found at $HOME\.claude-launcher or in the functions folder.' -ForegroundColor DarkGray
    Write-Host '  Checking the PowerShell profiles anyway...' -ForegroundColor DarkGray
}

# ------------------------------------------------------------ back up config
$backupDir = $null
if (-not $Purge -and -not $KeepConfig) {
    $keepFiles = @($profilesFile, $settingsFile) | Where-Object { Test-Path $_ }
    if ($keepFiles.Count -gt 0) {
        $backupDir = Join-Path $HOME ("claude-launcher-backup-" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        if ($PSCmdlet.ShouldProcess($backupDir, 'Back up profiles.json and ui.json')) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
            foreach ($file in $keepFiles) {
                Copy-Item $file $backupDir -Force
                Write-Step "backed up $(Split-Path $file -Leaf)"
            }
        }
    }
}

# ------------------------------------------- optionally delete Claude data dirs
if ($IncludeClaudeConfigDirs) {
    $configDirs = @()
    if (Test-Path $profilesFile) {
        try {
            $config = Get-Content $profilesFile -Raw | ConvertFrom-Json
            foreach ($entry in @($config.profiles)) {
                if ([string]::IsNullOrWhiteSpace($entry.configDir)) { continue }
                # Plain string replace: -replace would treat $HOME and backslashes
                # as regex/substitution syntax.
                $expanded = $entry.configDir.Replace('$HOME', $HOME).Replace('/', '\')
                if (Test-Path $expanded) { $configDirs += $expanded }
            }
        }
        catch {
            $warnings.Add("Could not read $profilesFile to find Claude config dirs: $($_.Exception.Message)")
        }
    }

    if ($configDirs.Count -eq 0) {
        Write-Step 'no Claude config dirs found to delete'
    }
    else {
        Write-Host ''
        Write-Host '  These folders hold your Claude Code history, settings and MCP servers:' -ForegroundColor Yellow
        foreach ($dir in $configDirs) {
            $size = 0
            try { $size = (Get-ChildItem $dir -Recurse -File -ErrorAction SilentlyContinue | Measure-Object -Property Length -Sum).Sum }
            catch { }
            Write-Host ("    {0}  ({1:N1} MB)" -f $dir, ($size / 1MB)) -ForegroundColor Yellow
        }

        if (Confirm-Destructive -Question '  Deleting these is irreversible.' -Expected 'delete') {
            foreach ($dir in $configDirs) {
                if ($PSCmdlet.ShouldProcess($dir, 'Remove Claude config dir')) {
                    Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
                    $removed.Add($dir)
                }
            }
        }
        else {
            Write-Step 'skipped Claude config dirs'
        }
    }
}

# ------------------------------------------------------- remove launcher files
if (Test-Path $launcherDir) {
    if ($KeepConfig) {
        # Drop the binary and runtime state, keep the user's own files.
        $keepNames = @('profiles.json', 'ui.json')
        foreach ($item in Get-ChildItem $launcherDir -Force) {
            if ($keepNames -contains $item.Name) { continue }
            if ($PSCmdlet.ShouldProcess($item.FullName, 'Remove')) {
                try {
                    Remove-Item $item.FullName -Recurse -Force
                    $removed.Add($item.FullName)
                }
                catch {
                    $warnings.Add("Could not remove $($item.FullName): $($_.Exception.Message)")
                }
            }
        }

        Write-Step 'kept profiles.json and ui.json'
    }
    else {
        # The exe may be locked by a launcher window that is still open.
        if (Test-Path $exePath) {
            for ($attempt = 1; $attempt -le 3; $attempt++) {
                try {
                    if ($PSCmdlet.ShouldProcess($exePath, 'Remove')) { Remove-Item $exePath -Force }
                    break
                }
                catch {
                    if ($attempt -eq 3) {
                        $warnings.Add("ClaudeLauncher.exe is in use. Close any running launcher window, then re-run this script.")
                    }
                    else {
                        Write-Step 'ClaudeLauncher.exe is in use, retrying...'
                        Start-Sleep -Seconds 2
                    }
                }
            }
        }

        if ($PSCmdlet.ShouldProcess($launcherDir, 'Remove directory')) {
            Remove-Item $launcherDir -Recurse -Force -ErrorAction SilentlyContinue
            if (Test-Path $launcherDir) {
                $warnings.Add("$launcherDir could not be fully removed. Close any running launcher and delete it manually.")
            }
            else {
                $removed.Add($launcherDir)
            }
        }
    }
}

foreach ($wrapper in $wrapperScripts) {
    if (-not (Test-Path $wrapper)) { continue }
    if ($PSCmdlet.ShouldProcess($wrapper, 'Remove')) {
        Remove-Item $wrapper -Force -ErrorAction SilentlyContinue
        $removed.Add($wrapper)
    }
}

# ------------------------------------------------- clean the PowerShell profiles
foreach ($profileScript in $profileScripts) {
    if (-not (Test-Path $profileScript)) { continue }

    $lines = @(Get-Content $profileScript)
    $kept = $lines | Where-Object { $_ -notmatch 'functions\\claude-launcher\.ps1' }

    if ($kept.Count -ne $lines.Count) {
        if ($PSCmdlet.ShouldProcess($profileScript, 'Remove the dot-source line')) {
            $backupPath = "$profileScript.claude-launcher.bak"
            Copy-Item $profileScript $backupPath -Force
            Set-Content -Path $profileScript -Value $kept -Encoding UTF8
            Write-Step "cleaned $(Split-Path $profileScript -Leaf) (backup: $(Split-Path $backupPath -Leaf))"
        }
    }

    # A manual install may have pasted the functions straight into the profile;
    # that cannot be removed safely by pattern, so point at it instead.
    $inline = Select-String -Path $profileScript -Pattern 'function\s+(claude-launcher|claude-work|claude-personal|Invoke-ClaudeLauncher)' -ErrorAction SilentlyContinue
    foreach ($hit in $inline) {
        $warnings.Add("$profileScript line $($hit.LineNumber) defines $($hit.Matches[0].Groups[1].Value) inline - remove that block by hand.")
    }
}

# --------------------------------------------------- stray environment variable
$userConfigDir = [Environment]::GetEnvironmentVariable('CLAUDE_CONFIG_DIR', 'User')
if (-not [string]::IsNullOrWhiteSpace($userConfigDir)) {
    Write-Host ''
    Write-Host "  CLAUDE_CONFIG_DIR is set for your user account: $userConfigDir" -ForegroundColor Yellow
    Write-Host '  v1.5.x never sets this; an older manual install probably did.' -ForegroundColor DarkGray
    Write-Host '  It still affects plain `claude` runs after uninstalling.' -ForegroundColor DarkGray

    if (Confirm-Destructive -Question '  Clear it?' -Expected 'yes') {
        if ($PSCmdlet.ShouldProcess('CLAUDE_CONFIG_DIR (User scope)', 'Clear')) {
            [Environment]::SetEnvironmentVariable('CLAUDE_CONFIG_DIR', $null, 'User')
            $removed.Add('CLAUDE_CONFIG_DIR (User environment variable)')
        }
    }
}

# ------------------------------------------------------------------- summary
Write-Host ''
if ($removed.Count -eq 0) {
    Write-Host '  Nothing to remove - Claude Launcher does not appear to be installed.' -ForegroundColor DarkGray
}
else {
    Write-Host "Removed $($removed.Count) item(s)." -ForegroundColor Green
    foreach ($item in $removed) { Write-Step $item }
}

if ($backupDir -and (Test-Path $backupDir)) {
    Write-Host ''
    Write-Host "Your profile definitions were saved to:" -ForegroundColor DarkGray
    Write-Host "  $backupDir" -ForegroundColor Cyan
}

if ($warnings.Count -gt 0) {
    Write-Host ''
    Write-Host 'Needs your attention:' -ForegroundColor Yellow
    foreach ($warning in $warnings) { Write-Host "  - $warning" -ForegroundColor Yellow }
}

Write-Host ''
Write-Host 'The claude-launcher / claude-work / claude-personal functions stay defined' -ForegroundColor DarkGray
Write-Host 'in this session until you open a new terminal.' -ForegroundColor DarkGray
Write-Host ''
