# Starts the DotWatcher server and client for local development.
# Each runs in its own window so their logs don't interleave and either can be
# restarted independently. Close a window (or Ctrl+C inside it) to stop that half.

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$serverDir = Join-Path $root 'server'
$clientDir = Join-Path $root 'client'
$serverPort = 8080

# Left running by a previous crashed/closed session, most often - surface who holds the port
# and let the caller choose: stop it, use a different port instead, or abort. Returns the port
# that ended up free, or $null if the user aborted.
function Resolve-ServerPort {
    param([int]$PreferredPort)

    $port = $PreferredPort
    while ($true) {
        $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        if (-not $conns) { return $port }

        $ownerPid = $conns.OwningProcess | Select-Object -Unique -First 1
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

Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "`$Host.UI.RawUI.WindowTitle = 'DotWatcher Server'; Set-Location '$serverDir'; `$env:ASPNETCORE_ENVIRONMENT = 'Development'; dotnet run --urls http://localhost:$serverPort"
)

Start-Process powershell -ArgumentList @(
    '-NoExit', '-Command',
    "`$Host.UI.RawUI.WindowTitle = 'DotWatcher Client'; Set-Location '$clientDir'; `$env:VITE_SERVER_PORT = '$serverPort'; npm run dev"
)

Write-Host "Server starting at http://localhost:$serverPort (see 'DotWatcher Server' window)"
Write-Host "Client starting at http://localhost:5173 (Vite picks the next free port if that's taken, see 'DotWatcher Client' window)"
