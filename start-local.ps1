# Starts the DotWatcher server and client for local development.
# Each runs in its own window so their logs don't interleave and either can be
# restarted independently. Close a window (or Ctrl+C inside it) to stop that half.
#
# Before starting anything, this script:
#   1. Requires Docker to already be running (it never starts Docker itself - the server needs
#      the Postgres container started separately via `docker compose up -d`, see the "Local
#      Postgres" section of server/README.md).
#   2. Finds and stops any existing server/client instance left over from a previous run, so
#      re-running this script always ends up with exactly one of each, cleanly bound to the
#      expected ports.

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $root 'server'
$clientDir = Join-Path $root 'client'

# Preferred port comes from the repo root's .env (shared with client/ and tools/simulator/ so
# every local tool agrees on it), falling back to 8080 if there's no override.
function Read-DotEnvValue {
    param([string]$Path, [string]$Key, [string]$Default)

    if (-not (Test-Path $Path)) { return $Default }

    foreach ($line in Get-Content $Path) {
        if ($line -match "^\s*$Key\s*=\s*(.+?)\s*$") { return $matches[1] }
    }
    return $Default
}

$serverPort = [int](Read-DotEnvValue -Path (Join-Path $root '.env') -Key 'VITE_SERVER_PORT' -Default '8080')
$clientPort = 5173 # Vite's default; the client normally binds here (see the final message below)

# $IsWindows/$IsMacOS are only defined under PowerShell Core (6+) - Windows PowerShell 5.1
# (the powershell.exe preinstalled on Windows) doesn't have them at all, so check the edition too.
$IsWindowsPlatform = $IsWindows -or $PSVersionTable.PSEdition -eq 'Desktop'

# Returns the PID listening on $port, or $null if nothing is. Windows uses Get-NetTCPConnection;
# macOS/Linux don't have that cmdlet, so fall back to lsof.
function Get-PortOwnerPid {
    param([int]$Port)

    if ($IsWindowsPlatform) {
        $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        return $conns.OwningProcess | Select-Object -Unique -First 1
    }

    $lsofPid = & lsof -nP -iTCP:$Port -sTCP:LISTEN -t 2>$null | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($lsofPid)) { return $null }
    return [int]$lsofPid
}

# True only when the Docker engine itself is reachable - `docker info` succeeds against a running
# daemon and fails (non-zero exit, or a missing-command error if Docker isn't installed at all)
# otherwise. Wrapped in try/catch since a missing `docker` executable raises a terminating
# CommandNotFoundException rather than just setting a non-zero exit code.
function Test-DockerRunning {
    try {
        docker info 1>$null 2>$null
        return $LASTEXITCODE -eq 0
    } catch {
        return $false
    }
}

if (-not (Test-DockerRunning)) {
    Write-Error "Docker isn't running. Start Docker Desktop (or your Docker engine), run 'docker compose up -d' from the repo root to bring up local Postgres, then re-run this script."
    exit 1
}

# Finds whatever's listening on $Port (almost always a previous run of this same script left
# over from a crashed/closed window) and stops it, so this script can always start fresh rather
# than colliding with - or silently reusing - a stale instance. Not scoped to dotnet.exe/node.exe
# specifically: whatever owns the port is what's actually in the way, named or not.
function Stop-ExistingInstance {
    param([int]$Port, [string]$Label)

    $ownerPid = Get-PortOwnerPid -Port $Port
    if (-not $ownerPid) {
        Write-Host "No existing $Label found on port $Port."
        return
    }

    $proc = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
    $name = if ($proc) { $proc.ProcessName } else { 'unknown process' }
    Write-Host "Stopping existing $Label ('$name', PID $ownerPid) on port $Port..."
    Stop-Process -Id $ownerPid -Force
    Start-Sleep -Milliseconds 500
}

Stop-ExistingInstance -Port $serverPort -Label 'DotWatcher server'
Stop-ExistingInstance -Port $clientPort -Label 'DotWatcher client'

# Opens a new window running $Command, titled $Title. Windows gets its own PowerShell window;
# macOS has no Start-Process window support, so a new Terminal.app tab is opened via osascript
# instead. Linux isn't handled - there's no single standard terminal emulator to target.
function Start-InNewWindow {
    param([string]$Title, [string]$Command)

    if ($IsWindowsPlatform) {
        Start-Process powershell -ArgumentList @(
            '-NoExit', '-Command',
            "`$Host.UI.RawUI.WindowTitle = '$Title'; $Command"
        )
        return
    }

    if ($IsMacOS) {
        # Terminal.app runs `do script` text in the user's default shell (zsh/bash), not
        # PowerShell, so re-invoke pwsh explicitly to run the (PowerShell-syntax) $Command.
        $pwshCommand = "pwsh -NoExit -Command `"& { `$Host.UI.RawUI.WindowTitle = '$Title'; $Command }`""
        $escaped = $pwshCommand.Replace('\', '\\').Replace('"', '\"')
        $osaScript = "tell application `"Terminal`" to do script `"$escaped`""
        & osascript -e $osaScript | Out-Null
        return
    }

    Write-Error "Start-InNewWindow isn't implemented for this platform - run the following manually:`n$Command"
    exit 1
}

Start-InNewWindow -Title 'DotWatcher Server' -Command "cd '$serverDir'; `$env:ASPNETCORE_ENVIRONMENT = 'Development'; dotnet run --urls http://localhost:$serverPort"

Start-InNewWindow -Title 'DotWatcher Client' -Command "cd '$clientDir'; `$env:VITE_SERVER_PORT = '$serverPort'; npm run dev"

Write-Host "Server starting at http://localhost:$serverPort (see 'DotWatcher Server' window)"
Write-Host "Client starting at http://localhost:$clientPort (see 'DotWatcher Client' window)"
