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

$wrapperTarget = $targetScript
Copy-Item (Join-Path $root 'scripts\claude-launcher.ps1') $wrapperTarget -Force
Unblock-File $wrapperTarget -ErrorAction SilentlyContinue

# --------------------------------------------------------- shell registration
# $PROFILE is only the profile of the host running this script - install.cmd
# runs Windows PowerShell - so a fresh machine that lives in PowerShell 7 would
# never load the wrapper. Register every host's profile, and drop a .cmd shim
# on PATH so cmd.exe and anything else that is not PowerShell can start it too.
function Get-ClaudeLauncherProfilePaths {
    $paths = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace([string]$PROFILE)) { $paths.Add([string]$PROFILE) }

    # Derived from $PROFILE rather than $HOME: OneDrive Known Folder Move
    # redirects Documents, and only the host knows where it really landed.
    $hostDir = Split-Path -Parent ([string]$PROFILE)
    if (-not [string]::IsNullOrWhiteSpace($hostDir)) {
        $documents = Split-Path -Parent $hostDir
        if (-not [string]::IsNullOrWhiteSpace($documents)) {
            $paths.Add((Join-Path $documents 'WindowsPowerShell\Microsoft.PowerShell_profile.ps1'))
            $paths.Add((Join-Path $documents 'PowerShell\Microsoft.PowerShell_profile.ps1'))
        }
    }

    # Select-Object -Unique is case-sensitive on 5.1; paths are not.
    $unique = New-Object System.Collections.Generic.List[string]
    foreach ($path in $paths) {
        if ($unique -notcontains $path) { $unique.Add($path) }
    }

    return @($unique)
}

function Register-ClaudeLauncherProfiles {
    param([string]$Line)

    foreach ($path in (Get-ClaudeLauncherProfilePaths)) {
        try {
            if (-not (Test-Path $path)) { New-Item -ItemType File -Path $path -Force | Out-Null }

            $content = @(Get-Content $path -ErrorAction SilentlyContinue)
            if ($content -contains $Line) {
                Write-Host "  already registered in $path" -ForegroundColor DarkGray
                continue
            }

            Add-Content $path "`r`n$Line"
            Write-Host "  registered in $path" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "  could not register in ${path}: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }
}

function Install-ClaudeLauncherShim {
    param([string]$LauncherDir, [string]$WrapperPath)

    # cmd.exe cannot see a PowerShell function, so the shim re-enters a shell -
    # pwsh where there is one, Windows PowerShell otherwise. It goes through
    # -File and a tiny entry script rather than -Command: splicing %* into a
    # -Command string drops the quotes around an argument that has spaces.
    $entry = Join-Path $LauncherDir 'claude-launcher-shim.ps1'
    $quoted = "'" + $WrapperPath.Replace("'", "''") + "'"
    Set-Content -Path $entry -Encoding UTF8 -Value @(
        '# Generated by the Claude Launcher installer; edits are overwritten on update.',
        'param([Parameter(ValueFromRemainingArguments = $true)]$Arguments)',
        '',
        ('$wrapper = ' + $quoted),
        'if (-not (Test-Path $wrapper)) {',
        '    Write-Host "claude-launcher is not installed correctly: $wrapper is missing." -ForegroundColor Red',
        '    exit 1',
        '}',
        '',
        '. $wrapper',
        'if ($null -eq $Arguments) { $Arguments = @() }',
        'claude-launcher @Arguments'
    )

    $shim = Join-Path $LauncherDir 'claude-launcher.cmd'
    Set-Content -Path $shim -Encoding ASCII -Value @(
        '@echo off',
        'rem Generated by the Claude Launcher installer; edits are overwritten on update.',
        'setlocal',
        ('set "CL_ENTRY=' + $entry + '"'),
        'if not exist "%CL_ENTRY%" (',
        '    echo claude-launcher is not installed correctly: "%CL_ENTRY%" is missing.',
        '    exit /b 1',
        ')',
        'set "CL_HOST=powershell"',
        'where pwsh >nul 2>&1',
        'if not errorlevel 1 set "CL_HOST=pwsh"',
        '%CL_HOST% -NoLogo -ExecutionPolicy Bypass -File "%CL_ENTRY%" %*',
        'exit /b %ERRORLEVEL%'
    )

    return $shim
}

function Add-ClaudeLauncherToPath {
    param([string]$Directory)

    # Through the registry, not [Environment]::SetEnvironmentVariable: reading
    # Path back that way expands %USERPROFILE% and friends, and writing the
    # expanded copy would freeze every other entry on the machine.
    $added = $false
    $key = $null
    try {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
        $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)

        $present = $false
        foreach ($part in ($raw -split ';')) {
            if ($part.Trim() -eq '') { continue }
            if ($part.Trim().TrimEnd('\') -eq $Directory.TrimEnd('\')) { $present = $true }
        }

        if (-not $present) {
            $kind = [Microsoft.Win32.RegistryValueKind]::ExpandString
            try { $kind = $key.GetValueKind('Path') } catch { }

            # Appended to the raw string rather than rebuilt from its parts: an
            # empty segment elsewhere in PATH is not ours to tidy away.
            $value = $raw
            if ($value -ne '' -and -not $value.EndsWith(';')) { $value += ';' }
            $key.SetValue('Path', ($value + $Directory), $kind)
            $added = $true
        }
    }
    finally {
        if ($key) { $key.Close() }
    }

    if (($env:Path -split ';') -notcontains $Directory) { $env:Path = "$env:Path;$Directory" }
    return $added
}

function Publish-ClaudeLauncherPathChange {
    # Without WM_SETTINGCHANGE, Explorer hands every new terminal the old
    # environment block until the next sign-out, so the shim looks missing.
    try {
        if (-not ('ClaudeLauncher.NativeEnv' -as [type])) {
            Add-Type -Namespace 'ClaudeLauncher' -Name 'NativeEnv' -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
public static extern System.IntPtr SendMessageTimeout(System.IntPtr hWnd, uint Msg, System.UIntPtr wParam,
    string lParam, uint fuFlags, uint uTimeout, out System.UIntPtr lpdwResult);
'@
        }

        $unused = [System.UIntPtr]::Zero
        [ClaudeLauncher.NativeEnv]::SendMessageTimeout([System.IntPtr]0xffff, 0x1A, [System.UIntPtr]::Zero,
            'Environment', 2, 5000, [ref]$unused) | Out-Null
    }
    catch { }
}

function Show-ClaudeLauncherPolicyWarning {
    # The installer itself runs under -ExecutionPolicy Bypass, so the process
    # scope says nothing about whether the user's own shell will load a profile.
    $effective = 'Undefined'
    foreach ($scope in @('MachinePolicy', 'UserPolicy', 'CurrentUser', 'LocalMachine')) {
        $value = [string](Get-ExecutionPolicy -Scope $scope)
        if ($value -ne 'Undefined') { $effective = $value; break }
    }

    if ($effective -eq 'Undefined') { $effective = 'Restricted' }
    if ($effective -ne 'Restricted' -and $effective -ne 'AllSigned') { return }

    Write-Host ''
    Write-Host "  The execution policy is $effective, so your PowerShell profile will not load" -ForegroundColor Yellow
    Write-Host '  and claude-launcher will look missing. Allow local scripts with:' -ForegroundColor Yellow
    Write-Host '    Set-ExecutionPolicy -Scope CurrentUser RemoteSigned' -ForegroundColor Cyan
}

Register-ClaudeLauncherProfiles -Line '. "$HOME\Documents\WindowsPowerShell\functions\claude-launcher.ps1"'

$shim = Install-ClaudeLauncherShim -LauncherDir $launcherDir -WrapperPath $wrapperTarget
if (Add-ClaudeLauncherToPath -Directory $launcherDir) {
    Publish-ClaudeLauncherPathChange
    Write-Host "  added $launcherDir to PATH" -ForegroundColor DarkGray
}
Write-Host "  cmd.exe shim at $shim" -ForegroundColor DarkGray

Show-ClaudeLauncherPolicyWarning

Write-Host ''
Write-Host "$(& (Join-Path $launcherDir 'ClaudeLauncher.exe') --version) installed successfully." -ForegroundColor Green
Write-Host ''
Write-Host 'Open a new terminal - PowerShell 7, Windows PowerShell or cmd.exe - and run:' -ForegroundColor DarkGray
Write-Host '  claude-launcher' -ForegroundColor Cyan
Write-Host ''
Write-Host 'In this window, reload first:' -ForegroundColor DarkGray
Write-Host '  . $PROFILE' -ForegroundColor Cyan
Write-Host ''
Write-Host 'Tip: the new UI uses 24-bit colors - Windows Terminal is recommended.' -ForegroundColor DarkGray
Write-Host ''
