import { useEffect, useMemo, useRef, useState } from 'react'
import type { RunnerPosition } from './types.ts'
import {
  earliestActivityMs,
  findAdaptiveCountdowns,
  findGpsSignalLoss,
  findRunnersWithGap,
  findSleepingRunners,
  isRangeCovered,
  latestActivityMs,
  livePollingError,
  maxOrNull,
  mergeIntoByRunner,
  mergeRange,
  parseNdjson,
  positionsAtCutoff,
  shouldPollLivePositions,
  shouldPollLivePositionsByInvite,
  type RunnerCountdown,
  type TimeRange,
} from './useSessionTimelineLogic.ts'
import { isSessionLive, LIVE_STALE_MS } from './sessionLiveness.ts'
import { apiHeaders } from './apiHeaders.ts'
import { usePageVisible } from './usePageVisible.ts'

const TICK_MS = 100
const WINDOW_MS = 10 * 60 * 1000 // default backward-fetch window when scrubbing into uncached history
const DRAG_SETTLE_MS = 500 // how long to wait after the user releases the scrubber before fetching

// Mirrors the hardcoded `interval` in ios/DotWatcher/DotWatcher/LocationManager.swift:154 —
// phones snap their sends to wall-clock boundaries (:00/:15/:30/:45 for 15s, via the same
// epoch-floor math as that file's `nextPostAt`), so polling shortly after each boundary reliably
// catches fresh data instead of polling on an arbitrary independent phase. This is expected to
// become configurable per-race later (e.g. 5 min for long races); when it does, this needs to
// come from the session/server, not stay a fixed client-side assumption — and if different
// runners in the same session could ever use different intervals, a single shared boundary
// target here won't cleanly serve all of them.
const PHONE_SEND_INTERVAL_MS = 15_000
const POLL_BOUNDARY_BUFFER_MS = 2_000 // grace period for the phone's POST to land before we fetch

// The next moment the client should poll: the first phone-send boundary after `now`, plus the
// buffer above. Recomputed fresh each cycle (see the live-poll effect) rather than accumulated
// from a fixed-period timer, the same self-correcting approach as LocationManager's tracking loop.
function msUntilNextPollBoundary(nowMs: number): number {
  const nextBoundary = (Math.floor(nowMs / PHONE_SEND_INTERVAL_MS) + 1) * PHONE_SEND_INTERVAL_MS
  return nextBoundary + POLL_BOUNDARY_BUFFER_MS - nowMs
}

export interface SessionTimelineState {
  positions: RunnerPosition[][] | undefined
  following: boolean
  scrubTimeMs: number | null
  runStartMs: number | null
  nowMs: number
  virtualNowMs: number
  isLive: boolean
  lastActivityMs: number | null
  pollIntervalMs: number
  runnersWithGpsSignalLoss: Set<string>
  runnersWithGap: Set<string>
  runnersSleeping: Set<string>
  runnerCountdowns: Map<string, RunnerCountdown>
  playing: boolean
  speed: number
  loading: boolean
  error: string | null
  invalidInvite: boolean
  dragTo: (ms: number) => void
  dragEnd: () => void
  goLive: () => void
  play: () => void
  pause: () => void
  setSpeed: (speed: number) => void
}

export function useSessionTimeline(
  sessionId: string | null,
  serverUrl: string,
  accessToken: string | null,
  inviteCode?: string | null,
): SessionTimelineState {
  const byInvite = shouldPollLivePositionsByInvite(inviteCode ?? null, accessToken)
  const byMembership = shouldPollLivePositions(sessionId, accessToken)
  const active = byInvite || byMembership
  const pageVisible = usePageVisible()

  const [byRunner, setByRunner] = useState<Map<string, RunnerPosition[]>>(new Map())
  const [fetchedRanges, setFetchedRanges] = useState<TimeRange[]>([])
  const [runStartMs, setRunStartMs] = useState<number | null>(null)
  const [metaLatestMs, setMetaLatestMs] = useState<number | null>(null)
  const [nowMs, setNowMs] = useState<number>(() => Date.now())
  const [scrubTimeMs, setScrubTimeMs] = useState<number | null>(null)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(10)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [invalidInvite, setInvalidInvite] = useState(false)

  // Falls back to the earliest live-polled position when the one-shot recording/meta fetch
  // missed the run (e.g. it 404'd because the viewer loaded before the runner's first ping,
  // and that fetch is never retried) — otherwise the scrubber would stay hidden for the rest
  // of the page's lifetime even once real position data starts arriving.
  const effectiveRunStartMs = runStartMs ?? earliestActivityMs(byRunner)

  const speedRef = useRef(speed)
  speedRef.current = speed
  const playingRef = useRef(false)
  const settleTimerRef = useRef<ReturnType<typeof setTimeout>>()
  const fetchedRangesRef = useRef<TimeRange[]>([])
  fetchedRangesRef.current = fetchedRanges
  const inFlightRef = useRef<Set<string>>(new Set())

  // Live-polling and scrub-driven window fetches both refetch overlapping ranges by design
  // (e.g. every live poll re-requests the same "latest position" endpoint), so logging every
  // update as it arrives would print the same points repeatedly. Tracking what's already been
  // logged per runner keeps the debug output to one line per genuinely new location.
  const loggedTimestampsRef = useRef<Map<string, Set<string>>>(new Map())
  function logNewPositions(updates: RunnerPosition[]) {
    for (const p of updates) {
      let seen = loggedTimestampsRef.current.get(p.runnerName)
      if (!seen) {
        seen = new Set()
        loggedTimestampsRef.current.set(p.runnerName, seen)
      }
      if (seen.has(p.timestamp)) continue
      seen.add(p.timestamp)
      console.debug(`[location] ${p.runnerName} lat=${p.latitude} lon=${p.longitude} heading=${p.heading} timestamp=${p.timestamp}`)
    }
  }

  const recordingBase = byInvite
    ? `${serverUrl}/session-invites/${inviteCode}`
    : sessionId
    ? `${serverUrl}/sessions/${sessionId}`
    : null
  const headers = apiHeaders(byInvite ? undefined : accessToken)

  // Reset all cached state when switching sessions/invites.
  useEffect(() => {
    setByRunner(new Map())
    setFetchedRanges([])
    setRunStartMs(null)
    setMetaLatestMs(null)
    setScrubTimeMs(null)
    setPlaying(false)
    setError(null)
    setInvalidInvite(false)
  }, [recordingBase])

  // Fetch scrubbable-range metadata once per session/invite so the scrubber can render a real
  // left bound before any position data has been fetched.
  useEffect(() => {
    if (!active || !recordingBase) return
    let cancelled = false

    fetch(`${recordingBase}/recording/meta`, { headers })
      .then(res => {
        if (cancelled) return null
        if (res.status === 404) return null
        if (!res.ok) throw new Error(`Failed to load recording metadata: HTTP ${res.status}`)
        return res.json()
      })
      .then(meta => {
        if (cancelled || !meta) return
        if (meta.runStartTimestamp) setRunStartMs(new Date(meta.runStartTimestamp).getTime())
        if (meta.latestTimestamp) setMetaLatestMs(new Date(meta.latestTimestamp).getTime())
      })
      .catch(err => {
        if (!cancelled) console.error('[useSessionTimeline] meta fetch failed', err)
      })

    return () => { cancelled = true }
  }, [active, recordingBase]) // eslint-disable-line react-hooks/exhaustive-deps

  // Independent wall clock, decoupled from poll success/failure. Previously `nowMs` was only
  // advanced inside a successful poll response, so a stalled poll (network blip, throttled
  // background tab, anything) froze `nowMs` at its last successful value — leaving `isLive`
  // stuck showing whatever it was right before the outage instead of correctly going stale.
  useEffect(() => {
    if (!active) return
    const id = setInterval(() => setNowMs(Date.now()), 1000)
    return () => clearInterval(id)
  }, [active])

  // Live-follow polling — only runs while following (scrubTimeMs === null) and the page is
  // actually visible. Pausing on hidden/backgrounded/locked (rather than just slowing down)
  // stops wasted requests outright; re-showing the page re-runs this effect, which fetches
  // immediately and resumes from the next aligned boundary, so the view catches back up right
  // away. Schedules itself via a self-correcting setTimeout targeting each next phone-send
  // boundary (see msUntilNextPollBoundary) rather than a fixed-period setInterval, so it stays
  // aligned to :00/:15/:30/:45 instead of drifting to whatever arbitrary phase this effect
  // happened to first run at.
  useEffect(() => {
    if (!active || scrubTimeMs !== null || !pageVisible) return
    let cancelled = false
    let stopped = false
    let timerId: ReturnType<typeof setTimeout> | undefined

    async function fetchAndUpdate() {
      try {
        const res = byInvite
          ? await fetch(`${serverUrl}/session-invites/${inviteCode}/locations`, { headers })
          : await fetch(`${serverUrl}/locations/${sessionId}`, { headers })
        if (cancelled) return
        if (!res.ok) {
          setError(livePollingError(res.status))
          if (byInvite && res.status === 404) {
            stopped = true
            setInvalidInvite(true)
          }
          return
        }
        const runnerGroups: RunnerPosition[][] = await res.json()
        if (cancelled) return
        setError(null)
        const updates = runnerGroups.flat()
        logNewPositions(updates)
        setByRunner(prev => mergeIntoByRunner(prev, updates))
      } catch {
        if (!cancelled) setError('Network error while loading live positions.')
      }
    }

    function scheduleNext() {
      if (cancelled || stopped) return
      timerId = setTimeout(async () => {
        await fetchAndUpdate()
        scheduleNext()
      }, msUntilNextPollBoundary(Date.now()))
    }

    fetchAndUpdate().then(scheduleNext)
    return () => {
      cancelled = true
      clearTimeout(timerId)
    }
  }, [active, scrubTimeMs, pageVisible, sessionId, serverUrl, accessToken, byInvite, inviteCode]) // eslint-disable-line react-hooks/exhaustive-deps

  function fetchWindow(sinceMs: number, untilMs: number) {
    if (!recordingBase) return
    const key = `${sinceMs}:${untilMs}`
    if (inFlightRef.current.has(key)) return
    inFlightRef.current.add(key)
    setLoading(true)

    const since = new Date(sinceMs).toISOString()
    const until = new Date(untilMs).toISOString()

    fetch(`${recordingBase}/recording?since=${encodeURIComponent(since)}&until=${encodeURIComponent(until)}`, {
      headers,
    })
      .then(async res => {
        if (!res.ok) throw new Error(`Failed to load recording window: HTTP ${res.status}`)
        const text = await res.text()
        const updates = parseNdjson(text)
        logNewPositions(updates)
        setByRunner(prev => mergeIntoByRunner(prev, updates))
        setFetchedRanges(prev => mergeRange(prev, { since: sinceMs, until: untilMs }))
      })
      .catch(err => console.error('[useSessionTimeline] window fetch failed', err))
      .finally(() => {
        inFlightRef.current.delete(key)
        setLoading(false)
      })
  }

  function ensureCovered(targetMs: number) {
    const floor = effectiveRunStartMs ?? targetMs
    const since = Math.max(floor, targetMs - WINDOW_MS)
    if (isRangeCovered(fetchedRangesRef.current, since, targetMs)) return
    fetchWindow(since, targetMs)
  }

  // Drag handling: dragTo just moves the (frozen) preview position; the actual fetch is
  // deferred until DRAG_SETTLE_MS after the user stops dragging, per product decision to pause
  // rather than fetch continuously mid-drag.
  function dragTo(ms: number) {
    if (playingRef.current) { playingRef.current = false; setPlaying(false) }
    if (settleTimerRef.current) clearTimeout(settleTimerRef.current)
    setScrubTimeMs(Math.max(effectiveRunStartMs ?? ms, Math.min(ms, nowMs)))
  }

  function dragEnd() {
    if (settleTimerRef.current) clearTimeout(settleTimerRef.current)
    settleTimerRef.current = setTimeout(() => {
      setScrubTimeMs(current => {
        if (current !== null) ensureCovered(current)
        return current
      })
    }, DRAG_SETTLE_MS)
  }

  function goLive() {
    if (settleTimerRef.current) clearTimeout(settleTimerRef.current)
    playingRef.current = false
    setPlaying(false)
    setScrubTimeMs(null)
  }

  function play() {
    if (scrubTimeMs === null) return
    playingRef.current = true
    setPlaying(true)
  }

  function pause() {
    playingRef.current = false
    setPlaying(false)
  }

  // Virtual clock driving playback once scrubbed. Rejoins live when it catches up to "now".
  // Also pauses (without touching `playing`/`scrubTimeMs`) while the page is hidden, so a
  // backgrounded replay doesn't keep ticking and issuing window fetches unseen — it resumes
  // from exactly where it left off once the page is visible again.
  useEffect(() => {
    if (!playing || !pageVisible) return
    playingRef.current = true
    const id = setInterval(() => {
      if (!playingRef.current) return
      setScrubTimeMs(t => {
        if (t === null) return t
        const next = t + TICK_MS * speedRef.current
        if (next >= nowMs) {
          playingRef.current = false
          setPlaying(false)
          return null // caught up to live — rejoin live-follow
        }
        ensureCovered(next)
        return next
      })
    }, TICK_MS)
    return () => { playingRef.current = false; clearInterval(id) }
  }, [playing, nowMs, pageVisible]) // eslint-disable-line react-hooks/exhaustive-deps

  // The playhead time: wall-clock while following live, the scrub position while replaying.
  const virtualNowMs = scrubTimeMs ?? nowMs

  const positions = useMemo((): RunnerPosition[][] | undefined => {
    const result = positionsAtCutoff(byRunner, virtualNowMs)
    return result.length > 0 ? result : undefined
  }, [byRunner, virtualNowMs])

  const polledLatestMs = useMemo(() => latestActivityMs(byRunner), [byRunner])
  const lastActivityMs = useMemo(() => maxOrNull(polledLatestMs, metaLatestMs), [polledLatestMs, metaLatestMs])
  const isLive = useMemo(() => isSessionLive(lastActivityMs, nowMs, LIVE_STALE_MS), [lastActivityMs, nowMs])

  // Flags runners whose device can't currently determine a heading — CoreLocation reports
  // heading as null when its course confidence is too low, which tends to coincide with the
  // position itself being untrustworthy (confirmed against a real session: the runner's dot
  // was visibly in the wrong place with no direction arrow at the same time). Not a Dot Watcher
  // bug, but worth telling viewers about so a wrong/frozen-looking dot doesn't look like the app
  // lost the plot. Stays flagged per-runner until GPS_JUMP_CLEAR_STREAK consecutive readings
  // with a real heading follow (see findGpsSignalLoss), not just the next single good one.
  const runnersWithGpsSignalLoss = useMemo(() => findGpsSignalLoss(byRunner), [byRunner])

  // Runners with a genuine mid-track gap right at the current playhead — see findRunnersWithGap
  // for why this is distinct from "hasn't reported yet" (already omitted from `positions`) and
  // from GPS signal loss (which is heading-quality based, not a data-gap check).
  const runnersWithGap = useMemo(() => findRunnersWithGap(byRunner, virtualNowMs), [byRunner, virtualNowMs])

  // Runners actively reporting but barely moving over the last SLEEPING_WINDOW_MS — distance
  // based, not elapsed-time based, so it never overlaps with runnersWithGap (which only fires on
  // absence of data): a runner can't be judged "not moving" from data that doesn't exist.
  const runnersSleeping = useMemo(() => findSleepingRunners(byRunner, virtualNowMs), [byRunner, virtualNowMs])

  // Live per-runner countdown to their next expected post — only for runners on a slower-than-
  // normal cadence (e.g. satellite), see findAdaptiveCountdowns. Recomputed every second via
  // virtualNowMs so it ticks down without any extra network requests, per the plan's design.
  const runnerCountdowns = useMemo(
    () => findAdaptiveCountdowns(byRunner, virtualNowMs, PHONE_SEND_INTERVAL_MS),
    [byRunner, virtualNowMs],
  )

  return {
    positions,
    following: scrubTimeMs === null,
    scrubTimeMs,
    runStartMs: effectiveRunStartMs,
    nowMs,
    virtualNowMs,
    isLive,
    lastActivityMs,
    pollIntervalMs: PHONE_SEND_INTERVAL_MS,
    runnersWithGpsSignalLoss,
    runnersWithGap,
    runnersSleeping,
    runnerCountdowns,
    playing,
    speed,
    loading,
    error,
    invalidInvite,
    dragTo,
    dragEnd,
    goLive,
    play,
    pause,
    setSpeed,
  }
}
