#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Creates a 10-minute, two-runner demo recording that deliberately walks through
    all four connectivity states the app has UI for: normal, a temporary GPS
    signal-loss window, a temporary missing-data gap, and a runner that stops
    reporting for good ("ended session").

.DESCRIPTION
    Unlike simulate-gps-track.ps1 (randomized bad-GPS windows, posted in real
    time), this script posts a fixed, hand-scripted schedule and backdates every
    position's timestamp instead of pacing itself with Start-Sleep - the whole
    10-minute recording is written in a couple of seconds, immediately
    replayable via the app's scrubber. The final tick's timestamp is ~now, so
    the session also reads as currently live.

    Runner 1 story: normal -> temporary GPS signal loss (null heading) -> normal.
    Runner 2 story: normal -> temporary missing-data gap -> normal -> permanent
    stop ("ended session" - no more updates for the rest of the run).

.PARAMETER Name1
    First runner's name/initials (default: SK)

.PARAMETER Name2
    Second runner's name/initials (default: AB)

.PARAMETER Server
    Server base URL (default: http://localhost:8080)

.PARAMETER SessionName
    Fixed session name to create/reuse (default: CONNDEMO)

.PARAMETER AdminToken
    Admin bearer token used to clear the session's recording (default: dev-token,
    the local dev value baked into server/appsettings.Development.json)

.EXAMPLE
    ./simulate-connectivity-demo.ps1

.EXAMPLE
    ./simulate-connectivity-demo.ps1 -Server http://localhost:8090
#>
[CmdletBinding()]
param(
    [string]$Name1 = "SK",
    [string]$Name2 = "AB",
    [string]$Server = "http://localhost:8080",
    [string]$SessionName = "CONNDEMO",
    [string]$AdminToken = "dev-token"
)

$ErrorActionPreference = "Stop"

$TestPassword = "testpass123"
$TickSeconds = 8
$TotalTicks = 75 # 75 * 8s = 600s = 10 minutes

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

function Register-Or-Login {
    param([string]$Username, [string]$DisplayName)
    $result = Invoke-Api -Method POST -Path "/auth/register" -Body @{
        username    = $Username
        password    = $TestPassword
        displayName = $DisplayName
    } -OkStatuses @(409)
    if ($null -ne $result) {
        Write-Host "  Registered new user: $Username"
        return $result
    }
    $result = Invoke-Api -Method POST -Path "/auth/login" -Body @{
        username = $Username
        password = $TestPassword
    }
    Write-Host "  Logged in as existing user: $Username"
    return $result
}

function Find-SessionByName {
    param([string]$Token, [string]$TargetName)
    $memberships = Invoke-Api -Method GET -Path "/me/sessions" -Token $Token
    foreach ($m in $memberships) {
        if ($m.sessionName.ToUpper() -eq $TargetName.ToUpper()) { return $m }
    }
    return $null
}

function Send-Location {
    param([string]$Token, [string]$SessionId, [double]$Lat, [double]$Lng, $Heading, [datetime]$Timestamp)
    $body = @{
        sessionId = $SessionId
        latitude  = $Lat
        longitude = $Lng
        heading   = $Heading
        timestamp = $Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
    }
    try {
        Invoke-Api -Method POST -Path "/location" -Body $body -Token $Token | Out-Null
    } catch {
        Write-Host "  [error posting location: $_]"
    }
}

# --- Fixed connectivity schedule (tick indices, inclusive) ---
# Runner 1: normal, then a signal-loss window, then normal again.
$r1SignalLossStart = 30
$r1SignalLossEnd = 37 # 8 ticks (64s) of null heading - clears after 3 good ticks following

# Runner 2: normal, a temporary missing-data gap, normal again, then a permanent stop.
$r2GapStart = 15
$r2GapEnd = 24     # 10 ticks (80s) with no update posted at all
$r2EndedFromTick = 50 # from here to the end, no more updates are ever posted

function State-For {
    param([int]$Tick, [int]$BadStart, [int]$BadEnd, [string]$BadKind)
    if ($Tick -ge $BadStart -and $Tick -le $BadEnd) { return $BadKind }
    return "normal"
}

Write-Host "Setting up a 10-minute connectivity demo ($TotalTicks ticks @ ${TickSeconds}s) for $Name1 and $Name2..."

$ownerUsername = "sim-$($Name1.ToLower())"
$owner = Register-Or-Login -Username $ownerUsername -DisplayName $Name1
$ownerToken = $owner.accessToken

$runner2Username = "sim-$($Name2.ToLower())"
$runner2 = Register-Or-Login -Username $runner2Username -DisplayName $Name2
$runner2Token = $runner2.accessToken

$sessionNameUpper = $SessionName.ToUpper()
$existing = Find-SessionByName -Token $ownerToken -TargetName $sessionNameUpper
if ($null -ne $existing) {
    $sessionId = $existing.sessionId
    $inviteCode = $existing.inviteCode
    Invoke-Api -Method DELETE -Path "/sessions/$sessionId/recording" -Token $AdminToken | Out-Null
    Write-Host "Reusing existing session: name=$sessionNameUpper inviteCode=$inviteCode (recording cleared)"
} else {
    $session = Invoke-Api -Method POST -Path "/sessions" -Body @{ sessionName = $sessionNameUpper; displayName = $null } -Token $ownerToken
    $sessionId = $session.sessionId
    $inviteCode = $session.inviteCode
    Write-Host "Session created: name=$sessionNameUpper inviteCode=$inviteCode"
}

Invoke-Api -Method POST -Path "/session-invites/$inviteCode/join" -Body @{ displayName = $Name1; role = "runner" } -Token $ownerToken | Out-Null
Invoke-Api -Method POST -Path "/session-invites/$inviteCode/join" -Body @{ displayName = $Name2; role = "runner" } -Token $runner2Token | Out-Null
Write-Host "  Runner 1 ($Name1) is the session owner; Runner 2 ($Name2) joined via invite code."
Write-Host ""
Write-Host "Session code: $sessionNameUpper  |  Invite code: $inviteCode"
Write-Host ""

# Every tick's timestamp is backdated from "now" rather than paced in real time, so the whole
# 10-minute recording exists (and is scrubbable) within seconds, while the last tick still
# lands close enough to "now" for the session to read as currently live.
$now = (Get-Date).ToUniversalTime()
$trackStart = $now.AddSeconds(-($TotalTicks - 1) * $TickSeconds)

# Both start near Wellington waterfront, moving along gentle NE paths with a little lateral
# drift so the paths diverge visibly on the map.
$lat1 = -41.2865; $lng1 = 174.7762
$lat2 = -41.2895; $lng2 = 174.7735
$step = 0.00013

for ($tick = 0; $tick -lt $TotalTicks; $tick++) {
    $lat1 += $step
    $lng1 += $step * 0.4
    $lat2 += $step * 0.9
    $lng2 += $step * 0.6
    $timestamp = $trackStart.AddSeconds($tick * $TickSeconds)

    $state1 = State-For -Tick $tick -BadStart $r1SignalLossStart -BadEnd $r1SignalLossEnd -BadKind "signal-loss"
    if ($state1 -eq "signal-loss") {
        Send-Location -Token $ownerToken -SessionId $sessionId -Lat $lat1 -Lng $lng1 -Heading $null -Timestamp $timestamp
    } else {
        Send-Location -Token $ownerToken -SessionId $sessionId -Lat $lat1 -Lng $lng1 -Heading 35 -Timestamp $timestamp
    }

    if ($tick -ge $r2EndedFromTick) {
        $state2 = "ended"
        # no update posted - runner 2 has stopped reporting for the rest of the run
    } else {
        $state2 = State-For -Tick $tick -BadStart $r2GapStart -BadEnd $r2GapEnd -BadKind "missing"
        if ($state2 -ne "missing") {
            Send-Location -Token $runner2Token -SessionId $sessionId -Lat $lat2 -Lng $lng2 -Heading 40 -Timestamp $timestamp
        }
    }

    $tickLabel = "{0,2}" -f $tick
    $s1Label = "{0,-12}" -f $state1
    $s2Label = "{0,-12}" -f $state2
    Write-Host "tick ${tickLabel}/$($TotalTicks - 1): $Name1=$s1Label  $Name2=$s2Label"
}

Write-Host ""
Write-Host "Demo recording complete."
Write-Host "  ${Name1}: normal (0-$($r1SignalLossStart - 1)) -> signal loss ($r1SignalLossStart-$r1SignalLossEnd) -> normal ($($r1SignalLossEnd + 1)-$($TotalTicks - 1), currently active)"
Write-Host "  ${Name2}: normal (0-$($r2GapStart - 1)) -> missing gap ($r2GapStart-$r2GapEnd) -> normal ($($r2GapEnd + 1)-$($r2EndedFromTick - 1)) -> ended session ($r2EndedFromTick-$($TotalTicks - 1), no more updates)"
