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
    param($Result, [string]$OpenIn = 'current')

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
    if ($OpenIn -and $OpenIn -ne 'current') {
        $opensIn = 'new tab'
        if ($OpenIn -eq 'right') { $opensIn = 'new pane - split right' }
        if ($OpenIn -eq 'down') { $opensIn = 'new pane - split down' }
        Write-ClaudeLauncherRow -Label 'Opens in' -Value $opensIn -Accent 'Cyan'
    }
    Write-Host ''

    if ($ansi) {
        Write-Host ("  $e[38;2;63;208;126m✓$e[0m $e[38;2;124;135;152mEnvironment ready - starting Claude Code$e[0m")
    }
    else {
        Write-Host '  [OK] Environment ready - starting Claude Code' -ForegroundColor Green
    }

    Write-Host ''
}

function ConvertTo-ClaudeLauncherPSLiteral {
    param([string]$Value)

    return "'" + ([string]$Value).Replace("'", "''") + "'"
}

function Get-ClaudeLauncherPaneShell {
    # pwsh defaults to UTF-8 and VT processing, both of which Claude's TUI wants.
    if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { return 'pwsh.exe' }
    return 'powershell.exe'
}

<#
    A trailing backslash before the closing quote is an escape to
    CommandLineToArgvW, so "C:\demo\" would swallow the quote. Drop it, unless
    the path is a bare drive root where the backslash is load-bearing.
#>
function ConvertTo-ClaudeLauncherWtPath {
    param([string]$Path)

    $value = [string]$Path
    if ($value.Length -gt 3 -and $value.EndsWith('\')) { return $value.TrimEnd('\') }
    return $value
}

<#
    Writes the script a spawned pane runs. Going through a file keeps the wt
    command line free of semicolons and nested quotes: wt would otherwise need
    `\;` escaping, on top of CommandLineToArgvW and PowerShell's own parser.
#>
function New-ClaudeLauncherPaneScript {
    param($Result, [string[]]$ExtraArgs)

    $panes = Join-Path $script:ClaudeLauncherRoot 'panes'
    if (-not (Test-Path $panes)) { New-Item -ItemType Directory -Path $panes -Force | Out-Null }

    # Stubs are disposable; do not let the folder grow without bound.
    Get-ChildItem $panes -Filter 'pane-*.ps1' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-2) } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $claudeArgs = @()
    switch ($Result.mode) {
        'resume' {
            $claudeArgs += '--resume'
            # A session id turns Claude's own picker into a direct resume.
            if ($Result.PSObject.Properties['sessionId'] -and $Result.sessionId) {
                $claudeArgs += [string]$Result.sessionId
            }
        }
        'continue' { $claudeArgs += '--continue' }
    }
    # Remote control lets claude.ai and the phone app type into this session.
    # The project name makes it findable in their session list.
    if ($Result.PSObject.Properties['remoteControl'] -and $Result.remoteControl -eq $true) {
        $claudeArgs += '--remote-control'
        $claudeArgs += [string]$Result.project
    }
    if ($ExtraArgs) { $claudeArgs += $ExtraArgs }

    $quoted = @()
    foreach ($a in $claudeArgs) { $quoted += (ConvertTo-ClaudeLauncherPSLiteral $a) }
    $argLiteral = '@(' + ($quoted -join ',') + ')'

    $lines = @(
        '# Generated by claude-launcher. Safe to delete.',
        ('$env:CLAUDE_CONFIG_DIR = ' + (ConvertTo-ClaudeLauncherPSLiteral $Result.configDir)),
        ('Set-Location -LiteralPath ' + (ConvertTo-ClaudeLauncherPSLiteral $Result.path)),
        ('$claudeArgs = ' + $argLiteral),
        '',
        'if (-not (Get-Command claude -ErrorAction SilentlyContinue)) {',
        '    Write-Host "claude was not found on PATH in this pane." -ForegroundColor Red',
        '    return',
        '}',
        '',
        'claude @claudeArgs',
        'Write-Host ""',
        'Write-Host "Claude exited. This pane stays open - close it with exit." -ForegroundColor DarkGray'
    )

    $file = Join-Path $panes ('pane-' + [guid]::NewGuid().ToString('N') + '.ps1')
    Set-Content -Path $file -Value $lines -Encoding UTF8
    return $file
}

<#
    Builds the wt.exe argument array. Returned as an array on purpose: splatting
    it with the call operator lets PowerShell quote each element, which it does
    correctly for spaces and for a bare ';' separator. Hand-joining into one
    string, or passing an array to Start-Process, both mangle paths with spaces.
#>
function New-ClaudeLauncherWtArgs {
    param($Result, [string]$PaneScript, [string]$OpenIn)

    $wtArgs = @()
    if ($env:WT_SESSION) { $wtArgs += @('-w', '0') } else { $wtArgs += @('-w', 'new') }

    if ($OpenIn -eq 'tab') {
        $wtArgs += 'new-tab'
    }
    else {
        $wtArgs += 'split-pane'
        # wt names the axis, not the direction: -V puts the new pane to the right.
        if ($OpenIn -eq 'right') { $wtArgs += '-V' } else { $wtArgs += '-H' }
    }

    $wtArgs += @('-d', (ConvertTo-ClaudeLauncherWtPath $Result.path))
    $wtArgs += @('--title', ("$($Result.icon) $($Result.project)"))
    $wtArgs += @((Get-ClaudeLauncherPaneShell), '-NoExit', '-File', $PaneScript)
    return , $wtArgs
}

function Start-ClaudeLauncherPane {
    param($Result, [string]$PaneScript, [string]$OpenIn)

    $wtArgs = New-ClaudeLauncherWtArgs -Result $Result -PaneScript $PaneScript -OpenIn $OpenIn
    if ($env:CLAUDE_LAUNCHER_WT_DRYRUN) { return , $wtArgs }

    & wt.exe @wtArgs
    return , $wtArgs
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

        # The launcher cannot replace the exe it is running from, so it asks for
        # the update on its way out and the installer runs here, after it is gone.
        if ($result.PSObject.Properties['action'] -and $result.action -eq 'update') {
            $wanted = ''
            if ($result.PSObject.Properties['version']) { $wanted = [string]$result.version }

            Write-Host ''
            if ($wanted) {
                Write-Host "  Updating Claude Launcher to $wanted..." -ForegroundColor Cyan
            }
            else {
                Write-Host '  Updating Claude Launcher...' -ForegroundColor Cyan
            }

            # This launcher has exited, but another window may still hold the
            # exe - the installer can only retry for a few seconds before it
            # gives up and leaves a .new file behind.
            $others = @(Get-Process -Name 'ClaudeLauncher' -ErrorAction SilentlyContinue)
            if ($others.Count -gt 0) {
                Write-Warning "$($others.Count) other Claude Launcher window(s) are open; close them if the update cannot replace the exe."
            }

            # A previous attempt that could not swap leaves this behind.
            Remove-Item (Join-Path $script:ClaudeLauncherRoot 'ClaudeLauncher.exe.new') -Force -ErrorAction SilentlyContinue

            try {
                $installer = 'https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1'
                & ([scriptblock]::Create((Invoke-RestMethod -Uri $installer)))

                Write-Host ''
                Write-Host '  Updated. Run claude-launcher again to start the new version.' -ForegroundColor Green
            }
            catch {
                Write-Warning "Could not update: $($_.Exception.Message)"
                Write-Host '  Run this yourself to try again:' -ForegroundColor DarkGray
                Write-Host '  irm https://raw.githubusercontent.com/chuongnd2612/claude-launcher/main/install-online.ps1 | iex' -ForegroundColor DarkGray
            }

            return
        }

        if (-not (Test-Path $result.path -PathType Container)) {
            throw "Project path does not exist: $($result.path)"
        }

        # Absent or unrecognised means the current console, so an older exe and a
        # newer wrapper (or the reverse) both keep working.
        $openIn = 'current'
        if ($result.PSObject.Properties['openIn'] -and $result.openIn) {
            $candidate = ([string]$result.openIn).Trim().ToLowerInvariant()
            if (@('current', 'tab', 'right', 'down') -contains $candidate) { $openIn = $candidate }
        }

        # An App Execution Alias is a zero-byte reparse point, so Test-Path lies here.
        if ($openIn -ne 'current' -and -not (Get-Command wt.exe -ErrorAction SilentlyContinue)) {
            Write-Warning 'Windows Terminal (wt.exe) was not found, so this cannot open in a tab or pane.'
            Write-Warning 'Install it with: winget install --id Microsoft.WindowsTerminal'
            Write-Warning 'Launching in this console instead.'
            $openIn = 'current'
        }

        if ($openIn -eq 'current') {
            $oldLocation = Get-Location
            $oldConfig = $env:CLAUDE_CONFIG_DIR

            try {
                Set-Location $result.path
                $env:CLAUDE_CONFIG_DIR = $result.configDir

                Write-ClaudeLauncherBanner -Result $result

                $claudeArgs = @()
                switch ($result.mode) {
                    'resume' {
                        $claudeArgs += '--resume'
                        if ($result.PSObject.Properties['sessionId'] -and $result.sessionId) {
                            $claudeArgs += [string]$result.sessionId
                        }
                    }
                    'continue' { $claudeArgs += '--continue' }
                }
                if ($result.PSObject.Properties['remoteControl'] -and $result.remoteControl -eq $true) {
                    $claudeArgs += '--remote-control'
                    $claudeArgs += [string]$result.project
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
        else {
            # Nothing to restore on this path: the pane gets its own location and
            # config dir from its stub, so this shell is left exactly as found.
            $paneScript = New-ClaudeLauncherPaneScript -Result $result -ExtraArgs $ClaudeArgs

            Write-ClaudeLauncherBanner -Result $result -OpenIn $openIn

            if (-not $env:WT_SESSION) {
                Write-Host '  Not running inside Windows Terminal - opening a new window.' -ForegroundColor DarkGray
            }

            $null = Start-ClaudeLauncherPane -Result $result -PaneScript $paneScript -OpenIn $openIn

            $where = 'a new tab'
            if ($openIn -eq 'right') { $where = 'a pane to the right' }
            if ($openIn -eq 'down') { $where = 'a pane below' }
            Write-Host ("  Claude is starting in {0}." -f $where) -ForegroundColor DarkGray
            Write-Host ''
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
