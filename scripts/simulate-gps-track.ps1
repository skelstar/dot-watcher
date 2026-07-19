#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Simulates a live Dot Watcher session for testing the GPS signal-loss warning,
    without needing a real device or a real run.

.DESCRIPTION
    Registers (or logs in as) two runners, creates (or reuses) a session, and posts
    a live-updating track for each over a set duration. The same two user accounts
    and the same session name are reused every run — accounts are only registered
    the first time (later runs just log in). By default the session's location
    history is cleared at the start of each run so every run starts from a clean
    track and is immediately replayable afterwards via the app's scrubber; pass
    -KeepHistory to skip the clear and append this run onto the existing recording
    instead (e.g. to build up a single long session you want to keep around, or to
    preserve a run for replay before starting a new one under a different
    -SessionName).

    Each runner independently cycles between three states so you can see the app
    react to different kinds of GPS trouble at different times:

      good    - a real heading is reported every tick (normal running)
      erratic - heading is null (device has a position but can't determine
                course - this is what the app's "Possible signal loss" warning
                and the red marker badge key off)
      nodata  - no update is posted at all for that tick (out-of-coverage /
                dropout - the dot simply stops moving, no warning is shown,
                since there is nothing to distinguish it from the runner pausing)

    Every run guarantees at least LeadInTicks worth of good GPS at the start, so
    there's always a clean, visible lead-in before the first warning appears.

.PARAMETER Name1
    First runner's name/initials (default: SK)

.PARAMETER Name2
    Second runner's name/initials (default: AB)

.PARAMETER Server
    Server base URL (default: http://localhost:8080)

.PARAMETER Minutes
    Track duration in minutes (default: 10)

.PARAMETER TickSeconds
    Seconds between location updates (default: 8)

.PARAMETER Seed
    Random seed, for a reproducible bad-GPS schedule

.PARAMETER SessionName
    Fixed session name to create/reuse (default: SIMTEST)

.PARAMETER AdminToken
    Admin bearer token used to clear the session's recording (default: dev-token,
    the local dev value baked into server/appsettings.Development.json)

.PARAMETER KeepHistory
    Don't clear the session's existing recording before this run (default: clear
    it, so every run starts fresh and is replayable)

.EXAMPLE
    ./simulate-gps-track.ps1

.EXAMPLE
    ./simulate-gps-track.ps1 -Minutes 0.5 -TickSeconds 1

.EXAMPLE
    ./simulate-gps-track.ps1 -Name1 JD -Name2 KL -Seed 42
#>
[CmdletBinding()]
param(
    [string]$Name1 = "SK",
    [string]$Name2 = "AB",
    [string]$Server = "http://localhost:8080",
    [double]$Minutes = 10,
    [int]$TickSeconds = 8,
    [Nullable[int]]$Seed = $null,
    [string]$SessionName = "SIMTEST",
    [string]$AdminToken = "dev-token",
    [switch]$KeepHistory
)

$ErrorActionPreference = "Stop"

$TestPassword = "testpass123"
# How many ticks of guaranteed good GPS to run before any bad window can start,
# so there's always a clean, visible lead-in before the first warning appears.
$LeadInTicks = 5

function Invoke-Api {
    param(
        [string]$Method,
        [string]$Path,
        $Body = $null,
        [string]$Token = $null,
        [int[]]$OkStatuses = @()
    )
    $headers = @{ "Content-Type" = "application/json" }
    if ($Token) { $headers["Authorization"] = "Bearer $Token" }

    $params = @{
        Method  = $Method
        Uri     = "$Server$Path"
        Headers = $headers
    }
    if ($null -ne $Body) {
        $params["Body"] = ($Body | ConvertTo-Json -Compress)
    }

    try {
        return Invoke-RestMethod @params
    } catch {
        $statusCode = $null
        if ($_.Exception.Response) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -and $OkStatuses -contains $statusCode) {
            return $null
        }
        $errBody = ""
        try {
            $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
            $errBody = $reader.ReadToEnd()
        } catch {}
        Write-Host ("  [HTTP {0} on {1} {2}: {3}]" -f $statusCode, $Method, $Path, $errBody)
        throw
    }
}

function Register-User {
    param([string]$Username, [string]$DisplayName)
    return Invoke-Api -Method POST -Path "/auth/register" -Body @{
        username    = $Username
        password    = $TestPassword
        displayName = $DisplayName
    } -OkStatuses @(409)
}

function Login-User {
    param([string]$Username)
    return Invoke-Api -Method POST -Path "/auth/login" -Body @{
        username = $Username
        password = $TestPassword
    }
}

function Register-Or-Login {
    param([string]$Username, [string]$DisplayName)
    $result = Register-User -Username $Username -DisplayName $DisplayName
    if ($null -ne $result) {
        Write-Host "  Registered new user: $Username"
        return $result
    }
    $result = Login-User -Username $Username
    Write-Host "  Logged in as existing user: $Username"
    return $result
}

function Find-SessionByName {
    param([string]$Token, [string]$TargetName)
    $memberships = Invoke-Api -Method GET -Path "/me/sessions" -Token $Token
    foreach ($m in $memberships) {
        if ($m.sessionName.ToUpper() -eq $TargetName.ToUpper()) {
            return $m
        }
    }
    return $null
}

function Clear-SessionRecording {
    param([string]$SessionId)
    Invoke-Api -Method DELETE -Path "/sessions/$SessionId/recording" -Token $AdminToken | Out-Null
}

function New-Session {
    param([string]$Token, [string]$TargetName)
    return Invoke-Api -Method POST -Path "/sessions" -Body @{
        sessionName = $TargetName
        displayName = $null
    } -Token $Token
}

function Join-Session {
    param([string]$Token, [string]$InviteCode, [string]$DisplayName)
    return Invoke-Api -Method POST -Path "/session-invites/$InviteCode/join" -Body @{
        displayName = $DisplayName
        role        = "runner"
    } -Token $Token
}

function Send-Location {
    param([string]$Token, [string]$SessionId, [double]$Lat, [double]$Lng, $Heading)
    $body = @{
        sessionId = $SessionId
        latitude  = $Lat
        longitude = $Lng
        heading   = $Heading
        timestamp = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    try {
        Invoke-Api -Method POST -Path "/location" -Body $body -Token $Token | Out-Null
    } catch {
        Write-Host "  [error posting location: $_]"
    }
}

function Get-RandomBadWindows {
    param([System.Random]$Rng, [int]$TotalTicks, [int]$SeedOffset)
    # Picks 2-3 non-overlapping bad windows (mix of erratic/nodata) scattered across
    # the track, each 3-8 ticks long, none starting before $LeadInTicks - guaranteeing
    # a clean start - and leaving plenty of good running between windows.
    $r = New-Object System.Random(($Rng.Next(0, 1000)) + $SeedOffset)
    $earliestStart = $LeadInTicks
    $windows = @()
    $targetCount = $r.Next(2, 4) # 2 or 3
    $attempts = 0

    while ($windows.Count -lt $targetCount -and $attempts -lt 50) {
        $attempts++
        $length = $r.Next(3, 9) # 3..8
        $latestStart = $TotalTicks - $length - 2
        if ($latestStart -lt $earliestStart) { break }

        $start = $r.Next($earliestStart, $latestStart + 1)
        $overlaps = $false
        foreach ($w in $windows) {
            if (-not (($start + $length -le $w.Start) -or ($start -ge $w.Start + $w.Length))) {
                $overlaps = $true
                break
            }
        }
        if ($overlaps) { continue }

        $kindRoll = $r.Next(0, 3) # 0,1 -> erratic (twice as likely), 2 -> nodata
        $kind = if ($kindRoll -lt 2) { "erratic" } else { "nodata" }
        $windows += [PSCustomObject]@{ Start = $start; Length = $length; Kind = $kind }
    }

    return $windows | Sort-Object Start
}

function Build-Schedule {
    param([int]$TotalTicks, $BadWindows)
    $schedule = [System.Collections.Generic.List[string]]::new()
    for ($i = 0; $i -lt $TotalTicks; $i++) { $schedule.Add("good") }
    foreach ($w in $BadWindows) {
        $endTick = [Math]::Min($w.Start + $w.Length, $TotalTicks) - 1
        for ($t = $w.Start; $t -le $endTick; $t++) { $schedule[$t] = $w.Kind }
    }
    return $schedule
}

function Show-WindowSummary {
    param([string]$Name, $Windows)
    if (-not $Windows -or $Windows.Count -eq 0) {
        Write-Host ("  {0}: no dropouts scheduled (all good)" -f $Name)
        return
    }
    $parts = $Windows | ForEach-Object { "$($_.Kind) @ tick $($_.Start)-$($_.Start + $_.Length - 1)" }
    Write-Host ("  {0}: {1}" -f $Name, ($parts -join '; '))
}

# --- Main ---

$rng = if ($null -ne $Seed) { New-Object System.Random($Seed) } else { New-Object System.Random }
$totalTicks = [int](($Minutes * 60) / $TickSeconds)
$sessionNameUpper = $SessionName.ToUpper()

Write-Host "Setting up a $([Math]::Round($Minutes))-minute session ($totalTicks ticks @ ${TickSeconds}s) for $Name1 and $Name2..."

$ownerUsername = "sim-$($Name1.ToLower())"
$owner = Register-Or-Login -Username $ownerUsername -DisplayName $Name1
$ownerToken = $owner.accessToken

$runner2Username = "sim-$($Name2.ToLower())"
$runner2 = Register-Or-Login -Username $runner2Username -DisplayName $Name2
$runner2Token = $runner2.accessToken

$existing = Find-SessionByName -Token $ownerToken -TargetName $sessionNameUpper
if ($null -ne $existing) {
    $sessionId = $existing.sessionId
    $inviteCode = $existing.inviteCode
    if ($KeepHistory) {
        Write-Host "Reusing existing session: name=$sessionNameUpper inviteCode=$inviteCode (keeping existing recording — this run will append to it)"
    } else {
        Clear-SessionRecording -SessionId $sessionId
        Write-Host "Reusing existing session: name=$sessionNameUpper inviteCode=$inviteCode (recording cleared)"
    }
} else {
    $session = New-Session -Token $ownerToken -TargetName $sessionNameUpper
    $sessionId = $session.sessionId
    $inviteCode = $session.inviteCode
    Write-Host "Session created: name=$sessionNameUpper inviteCode=$inviteCode"
}
Write-Host "  Runner 1 ($Name1) is the session owner."

Join-Session -Token $ownerToken -InviteCode $inviteCode -DisplayName $Name1 | Out-Null
Join-Session -Token $runner2Token -InviteCode $inviteCode -DisplayName $Name2 | Out-Null
Write-Host "  Runner 2 ($Name2) joined via invite code."

Write-Host ""
Write-Host "Watch at: http://localhost:5174/$sessionNameUpper  (or via invite code $inviteCode)"
Write-Host ""

# Both start near Wellington waterfront, moving along gentle NE paths with a
# little lateral drift so the paths diverge visibly on the map.
$lat1 = -41.2865; $lng1 = 174.7762
$lat2 = -41.2895; $lng2 = 174.7735
$step = 0.00013 # roughly 15m per tick along the path

$bad1 = Get-RandomBadWindows -Rng $rng -TotalTicks $totalTicks -SeedOffset 1
$bad2 = Get-RandomBadWindows -Rng $rng -TotalTicks $totalTicks -SeedOffset 2
$schedule1 = Build-Schedule -TotalTicks $totalTicks -BadWindows $bad1
$schedule2 = Build-Schedule -TotalTicks $totalTicks -BadWindows $bad2

Show-WindowSummary -Name $Name1 -Windows $bad1
Show-WindowSummary -Name $Name2 -Windows $bad2
Write-Host ""

for ($tick = 0; $tick -lt $totalTicks; $tick++) {
    $lat1 += $step
    $lng1 += $step * 0.4
    $lat2 += $step * 0.9
    $lng2 += $step * 0.6

    $state1 = $schedule1[$tick]
    $state2 = $schedule2[$tick]

    if ($state1 -ne "nodata") {
        $heading1 = if ($state1 -eq "erratic") { $null } else { 35 }
        Send-Location -Token $ownerToken -SessionId $sessionId -Lat $lat1 -Lng $lng1 -Heading $heading1
    }
    if ($state2 -ne "nodata") {
        $heading2 = if ($state2 -eq "erratic") { $null } else { 40 }
        Send-Location -Token $runner2Token -SessionId $sessionId -Lat $lat2 -Lng $lng2 -Heading $heading2
    }

    $tickLabel = "{0,2}" -f $tick
    $totalLabel = $totalTicks - 1
    $s1Label = "{0,-8}" -f $state1
    $s2Label = "{0,-8}" -f $state2
    Write-Host "tick ${tickLabel}/${totalLabel}: $Name1=$s1Label  $Name2=$s2Label"

    Start-Sleep -Seconds $TickSeconds
}

Write-Host ""
Write-Host "Simulation complete."
