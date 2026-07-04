param(
    [string]$ServerRoot = "D:\server\CS2Server",
    [switch]$BuildOnly,
    [switch]$ForceLive
)

$ErrorActionPreference = "Stop"

$utf8 = [System.Text.UTF8Encoding]::new($false)

[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectFile = Join-Path $ProjectRoot "Jailbreak.csproj"
$BuildPath = Join-Path $ProjectRoot "bin\Release\net10.0"
$PluginPath = Join-Path $ServerRoot "game\csgo\addons\counterstrikesharp\plugins\Jailbreak"
$ServerExePath = Join-Path $ServerRoot "game\bin\win64\cs2.exe"

$serverExe = $null

if (Test-Path $ServerExePath) {
    $serverExe = Get-CimInstance Win32_Process -Filter "Name = 'cs2.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ExecutablePath -eq $ServerExePath -or
            $_.CommandLine -like "*$ServerRoot*"
        } |
        Select-Object -First 1

    if (-not $serverExe) {
        $serverExe = Get-Process -Name "cs2" -ErrorAction SilentlyContinue |
            Where-Object {
                try {
                    $_.Path -eq $ServerExePath
                }
                catch {
                    $true
                }
            } |
            Select-Object -First 1
    }
}

if ($serverExe -and -not $ForceLive -and -not $BuildOnly) {
    $serverPid = if ($serverExe.ProcessId) { $serverExe.ProcessId } else { $serverExe.Id }
    $serverPath = if ($serverExe.ExecutablePath) { $serverExe.ExecutablePath } else { $serverExe.Path }

    Write-Warning "CS2 server is running: PID=$serverPid, Path=$serverPath"
    Write-Warning "Live-copying plugin DLLs can leave CounterStrikeSharp with mixed old/new plugin state."
    Write-Warning "Stop the server, or rerun with -ForceLive only if you intentionally want a live hot-reload copy."
    exit 2
}

if ($serverExe -and $ForceLive) {
    Write-Warning "ForceLive enabled while CS2 server is running. A server restart or explicit plugin reload is still recommended."
}

Write-Host "[1/4] Building Release"
dotnet build $ProjectFile -c Release

if ($BuildOnly) {
    Write-Host ""
    Write-Host "Build completed only. No files were copied."
    Write-Host "Output:"
    Write-Host $BuildPath
    exit 0
}

Write-Host "[2/4] Creating plugin directory"
New-Item -ItemType Directory -Force -Path $PluginPath | Out-Null

Write-Host "[3/4] Copying plugin files"

$Files = @(
    "Jailbreak.dll",
    "Jailbreak.deps.json",
    "Jailbreak.pdb"
)

foreach ($File in $Files) {
    $Source = Join-Path $BuildPath $File

    if (Test-Path $Source) {
        Copy-Item $Source $PluginPath -Force
        Write-Host "  Copied: $File"
    }
    else {
        Write-Warning "  Missing file: $Source"
    }
}

Write-Host "[4/4] Copying localization files"
$LangSource = Join-Path $BuildPath "lang"
$LangTarget = Join-Path $PluginPath "lang"

if (Test-Path $LangSource) {
    New-Item -ItemType Directory -Force -Path $LangTarget | Out-Null
    Copy-Item (Join-Path $LangSource "*") $LangTarget -Recurse -Force
    Write-Host "  Copied: lang"
}
else {
    Write-Warning "  Missing directory: $LangSource"
}

Write-Host ""
Write-Host "Deployment completed:"
Write-Host $PluginPath
Write-Host ""
if ($serverExe) {
    Write-Host "IMPORTANT: Server was running during deployment."
    Write-Host "Restart the server or fully unload/load the plugin before testing."
    Write-Host ""
}
Write-Host "Check these commands in the server console:"
Write-Host "  meta version"
Write-Host "  meta list"
Write-Host "  css_plugins list"
Write-Host "  css_jb version"
Write-Host "  css_jb status"
