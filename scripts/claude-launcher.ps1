# Claude Launcher PowerShell host wrapper.
# Compatible with Windows PowerShell 5.1+.
# QuickPaths remains the single source of truth for projects.

$script:ClaudeLauncherRoot = Join-Path $HOME '.claude-launcher'
$script:ClaudeLauncherProfiles = Join-Path $script:ClaudeLauncherRoot 'profiles.json'
$script:ClaudeLauncherBin = Join-Path $script:ClaudeLauncherRoot 'ClaudeLauncher.exe'

function Test-ClaudeLauncherAnsi {
    if ($env:WT_SESSION) { return $true }
    if ($env:TERM_PROGRAM) { return $true }
    try { return [bool]$Host.UI.SupportsVirtualTerminal } catch { return $false }
}

function Write-ClaudeLauncherGradient {
    param(
        [string]$Text,
        [int[]]$From = @(74, 158, 255),
        [int[]]$To = @(199, 125, 255)
    )

    $e = [char]27
    $len = [Math]::Max(1, $Text.Length - 1)
    $line = ''

    for ($i = 0; $i -lt $Text.Length; $i++) {
        $t = $i / $len
        $r = [int][Math]::Round($From[0] + ($To[0] - $From[0]) * $t)
        $g = [int][Math]::Round($From[1] + ($To[1] - $From[1]) * $t)
        $b = [int][Math]::Round($From[2] + ($To[2] - $From[2]) * $t)
        $line += "$e[1;38;2;$r;$g;$b" + 'm' + $Text[$i]
    }

    Write-Host ($line + "$e[0m")
}

function Write-ClaudeLauncherRow {
    param(
        [string]$Label,
        [string]$Value,
        [string]$Accent = 'White'
    )

    $e = [char]27
    if (Test-ClaudeLauncherAnsi) {
        Write-Host ("  $e[38;2;78;87;102m" + $Label.PadRight(11) + "$e[0m$e[38;2;230;237;246m$Value$e[0m")
    }
    else {
        Write-Host ('  ' + $Label.PadRight(11)) -NoNewline -ForegroundColor DarkGray
        Write-Host $Value -ForegroundColor $Accent
    }
}

function Write-ClaudeLauncherBanner {
    param($Result)

    $e = [char]27
    $ansi = Test-ClaudeLauncherAnsi

    Write-Host ''
    if ($ansi) {
        Write-ClaudeLauncherGradient -Text '  ✦  C L A U D E   L A U N C H E R'
        Write-Host ("  $e[38;2;27;33;43m" + ('─' * 58) + "$e[0m")
    }
    else {
        Write-Host '  *  CLAUDE LAUNCHER' -ForegroundColor Cyan
        Write-Host ('  ' + ('-' * 58)) -ForegroundColor DarkGray
    }

    Write-Host ''
    Write-ClaudeLauncherRow -Label 'Profile' -Value "$($Result.icon)  $($Result.label)"
    Write-ClaudeLauncherRow -Label 'Project' -Value $Result.project
    Write-ClaudeLauncherRow -Label 'Directory' -Value $Result.path
    Write-ClaudeLauncherRow -Label 'Config' -Value $Result.configDir
    Write-ClaudeLauncherRow -Label 'Session' -Value $Result.mode -Accent 'Yellow'
    Write-Host ''

    if ($ansi) {
        Write-Host ("  $e[38;2;63;208;126m✓$e[0m $e[38;2;124;135;152mEnvironment ready - starting Claude Code$e[0m")
    }
    else {
        Write-Host '  [OK] Environment ready - starting Claude Code' -ForegroundColor Green
    }

    Write-Host ''
}

function Initialize-ClaudeLauncher {
    if (-not (Test-Path $script:ClaudeLauncherRoot)) {
        New-Item -ItemType Directory -Path $script:ClaudeLauncherRoot -Force | Out-Null
    }

    if (-not (Test-Path $script:ClaudeLauncherProfiles)) {
        $default = @'
{
  "profiles": [
    {
      "name": "work",
      "label": "Work",
      "icon": "W",
      "configDir": "$HOME/.claude-work",
      "description": "Default profile for work projects"
    },
    {
      "name": "personal",
      "label": "Personal",
      "icon": "P",
      "configDir": "$HOME/.claude-personal",
      "description": "Personal profile"
    }
  ]
}
'@
        Set-Content -Path $script:ClaudeLauncherProfiles -Value $default -Encoding UTF8
    }

    if (-not (Test-Path $script:ClaudeLauncherBin)) {
        throw "ClaudeLauncher.exe was not found at $script:ClaudeLauncherBin. Run install.cmd or install.ps1 first."
    }
}

function Get-ClaudeLauncherProfiles {
    Initialize-ClaudeLauncher
    $config = Get-Content $script:ClaudeLauncherProfiles -Raw | ConvertFrom-Json
    return @($config.profiles)
}

function Invoke-ClaudeLauncher {
    [CmdletBinding()]
    param(
        [Alias('Profile')]
        [string]$ProfileName,
        [string]$Project,
        [ValidateSet('new','resume','continue')]
        [string]$Mode,
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$ClaudeArgs
    )

    Initialize-ClaudeLauncher

    $stateFile = Join-Path $script:ClaudeLauncherRoot 'state.json'
    $resultFile = Join-Path $script:ClaudeLauncherRoot 'result.json'

    $projects = @()
    if ($null -ne $QuickPaths) {
        foreach ($key in ($QuickPaths.Keys | Sort-Object)) {
            $projects += [pscustomobject]@{
                name = [string]$key
                path = [string]$QuickPaths[$key]
            }
        }
    }

    [pscustomobject]@{
        profiles = @(Get-ClaudeLauncherProfiles)
        projects = @($projects)
    } | ConvertTo-Json -Depth 10 | Set-Content $stateFile -Encoding UTF8

    Remove-Item $resultFile -Force -ErrorAction SilentlyContinue

    $env:CLAUDE_LAUNCHER_PROFILE = $ProfileName
    $env:CLAUDE_LAUNCHER_PROJECT = $Project
    if ($Mode) { $env:CLAUDE_LAUNCHER_MODE = $Mode } else { $env:CLAUDE_LAUNCHER_MODE = '' }
    # Explicitly share the state/result/profile files with the native TUI so the
    # launcher can also append profiles created from the "Add profile" screen.
    $env:CLAUDE_LAUNCHER_STATE = $stateFile
    $env:CLAUDE_LAUNCHER_RESULT = $resultFile
    $env:CLAUDE_LAUNCHER_PROFILES = $script:ClaudeLauncherProfiles

    try {
        & $script:ClaudeLauncherBin
        if ($LASTEXITCODE -ne 0) { return }
        if (-not (Test-Path $resultFile)) { return }

        $result = Get-Content $resultFile -Raw | ConvertFrom-Json
        $oldLocation = Get-Location
        $oldConfig = $env:CLAUDE_CONFIG_DIR

        try {
            if (-not (Test-Path $result.path -PathType Container)) {
                throw "Project path does not exist: $($result.path)"
            }

            Set-Location $result.path
            $env:CLAUDE_CONFIG_DIR = $result.configDir

            Write-ClaudeLauncherBanner -Result $result

            $claudeArgs = @()
            switch ($result.mode) {
                'resume' { $claudeArgs += '--resume' }
                'continue' { $claudeArgs += '--continue' }
            }
            if ($ClaudeArgs) { $claudeArgs += $ClaudeArgs }
            & claude @claudeArgs
        }
        finally {
            Set-Location $oldLocation
            if ($null -eq $oldConfig) {
                Remove-Item Env:CLAUDE_CONFIG_DIR -ErrorAction SilentlyContinue
            }
            else {
                $env:CLAUDE_CONFIG_DIR = $oldConfig
            }
        }
    }
    finally {
        Remove-Item $stateFile -Force -ErrorAction SilentlyContinue
        Remove-Item $resultFile -Force -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_PROFILE -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_PROJECT -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_MODE -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_STATE -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_RESULT -ErrorAction SilentlyContinue
        Remove-Item Env:CLAUDE_LAUNCHER_PROFILES -ErrorAction SilentlyContinue
    }
}

function claude-launcher {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$ClaudeArgs)
    Invoke-ClaudeLauncher -ClaudeArgs $ClaudeArgs
}

function Resolve-ClaudeShortcutArguments {
    param(
        [string[]]$Arguments
    )

    $mode = 'new'
    $remaining = @()

    foreach ($arg in @($Arguments)) {
        switch ($arg) {
            '--resume' { $mode = 'resume' }
            '--continue' { $mode = 'continue' }
            default { $remaining += $arg }
        }
    }

    return @{ Mode = $mode; Arguments = $remaining }
}

function claude-work {
    param(
        [string]$Project,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$ClaudeArgs
    )

    $parsed = Resolve-ClaudeShortcutArguments $ClaudeArgs

    if ($Project) {
        Invoke-ClaudeLauncher -ProfileName 'work' -Project $Project -Mode $parsed.Mode -ClaudeArgs $parsed.Arguments
    }
    else {
        Invoke-ClaudeLauncher -ProfileName 'work' -ClaudeArgs $ClaudeArgs
    }
}

function claude-personal {
    param(
        [string]$Project,
        [Parameter(ValueFromRemainingArguments = $true)][string[]]$ClaudeArgs
    )

    $parsed = Resolve-ClaudeShortcutArguments $ClaudeArgs

    if ($Project) {
        Invoke-ClaudeLauncher -ProfileName 'personal' -Project $Project -Mode $parsed.Mode -ClaudeArgs $parsed.Arguments
    }
    else {
        Invoke-ClaudeLauncher -ProfileName 'personal' -ClaudeArgs $ClaudeArgs
    }
}
