# Starts the DotWatcher server and client for local development.
# Each runs in its own window so their logs don't interleave and either can be
# restarted independently. Close a window (or Ctrl+C inside it) to stop that half.

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

# Left running by a previous crashed/closed session, most often - surface who holds the port
# and let the caller choose: stop it, use a different port instead, or abort. Returns the port
# that ended up free, or $null if the user aborted.
function Resolve-ServerPort {
    param([int]$PreferredPort)

    $port = $PreferredPort
    while ($true) {
        $ownerPid = Get-PortOwnerPid -Port $port
        if (-not $ownerPid) { return $port }

        $proc = Get-Process -Id $ownerPid -ErrorAction SilentlyContinue
        $name = if ($proc) { $proc.ProcessName } else { 'unknown process' }
        Write-Warning "Port $port is already in use by '$name' (PID $ownerPid)."

        switch -Regex (Read-Host "[S]top it, [P]ick a different port, or [A]bort? (S/P/A)") {
            '^[Ss]' {
                Stop-Process -Id $ownerPid -Force
                Start-Sleep -Milliseconds 500
            }
            '^[Pp]' {
                $next = Read-Host "Port to use instead (blank = $($port + 1))"
                if ([string]::IsNullOrWhiteSpace($next)) {
                    $port++
                } elseif ($next -match '^\d+$') {
                    $port = [int]$next
                } else {
                    Write-Warning "'$next' isn't a valid port number."
                }
            }
            default {
                return $null
            }
        }
    }
}

$serverPort = Resolve-ServerPort -PreferredPort $serverPort
if (-not $serverPort) {
    Write-Error "No server port selected - aborting."
    exit 1
}

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
Write-Host "Client starting at http://localhost:5173 (Vite picks the next free port if that's taken, see 'DotWatcher Client' window)"
