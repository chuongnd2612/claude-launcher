# Builds the standalone ClaudeLauncher.exe that the GitHub release ships.
# The result runs on a machine with no .NET installed at all.
#
#   .\scripts\publish-standalone.ps1
#   .\scripts\publish-standalone.ps1 -Version 1.5.1 -Output C:\temp\out
[CmdletBinding()]
param(
    [string]$Version,
    [string]$Runtime = 'win-x64',
    [string]$Output = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'src\ClaudeLauncher.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK 8 is required to build. Contributors need it; end users do not.'
}

if (-not $Output) { $Output = Join-Path $root "dist\$Runtime" }
if (Test-Path $Output) { Remove-Item $Output -Recurse -Force }
New-Item -ItemType Directory -Path $Output -Force | Out-Null

$arguments = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', 'true',
    '-o', $Output,
    '--nologo',
    # src/NuGet.config clears all sources to keep the normal build offline.
    # Self-contained publishing needs the runtime pack, so re-add nuget.org here.
    '--source', 'https://api.nuget.org/v3/index.json',
    '-p:PublishSingleFile=true',
    '-p:EnableCompressionInSingleFile=true',
    '-p:DebugType=none',
    '-p:GenerateDocumentationFile=false'
)

if ($Version) { $arguments += "-p:Version=$Version" }

& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $Output 'ClaudeLauncher.exe'
if (-not (Test-Path $exe)) { throw "Expected $exe but it was not produced." }

$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLower()
Set-Content -Path "$exe.sha256" -Value "$hash  ClaudeLauncher.exe" -Encoding ASCII

$size = [Math]::Round((Get-Item $exe).Length / 1MB, 1)

Write-Host ''
Write-Host "Standalone build ready: $exe" -ForegroundColor Green
Write-Host "  size    $size MB" -ForegroundColor DarkGray
Write-Host "  sha256  $hash" -ForegroundColor DarkGray
Write-Host "  version $(& $exe --version)" -ForegroundColor DarkGray
Write-Host ''
