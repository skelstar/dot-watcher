import { useEffect, useMemo, useRef, useState } from 'react'
import type { RunnerPosition } from './types.ts'
import {
  isRangeCovered,
  livePollingError,
  mergeIntoByRunner,
  mergeRange,
  parseNdjson,
  positionsAtCutoff,
  shouldPollLivePositions,
  shouldPollLivePositionsByInvite,
  type TimeRange,
} from './useSessionTimelineLogic.ts'

const TICK_MS = 100
const WINDOW_MS = 10 * 60 * 1000 // default backward-fetch window when scrubbing into uncached history
const DRAG_SETTLE_MS = 500 // how long to wait after the user releases the scrubber before fetching

export interface SessionTimelineState {
  positions: RunnerPosition[][] | undefined
  following: boolean
  scrubTimeMs: number | null
  runStartMs: number | null
  nowMs: number
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
  pollIntervalMs: number,
  inviteCode?: string | null,
): SessionTimelineState {
  const byInvite = shouldPollLivePositionsByInvite(inviteCode ?? null, accessToken)
  const byMembership = shouldPollLivePositions(sessionId, accessToken)
  const active = byInvite || byMembership

  const [byRunner, setByRunner] = useState<Map<string, RunnerPosition[]>>(new Map())
  const [fetchedRanges, setFetchedRanges] = useState<TimeRange[]>([])
  const [runStartMs, setRunStartMs] = useState<number | null>(null)
  const [nowMs, setNowMs] = useState<number>(() => Date.now())
  const [scrubTimeMs, setScrubTimeMs] = useState<number | null>(null)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(10)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [invalidInvite, setInvalidInvite] = useState(false)

  const speedRef = useRef(speed)
  speedRef.current = speed
  const playingRef = useRef(false)
  const settleTimerRef = useRef<ReturnType<typeof setTimeout>>()
  const fetchedRangesRef = useRef<TimeRange[]>([])
  fetchedRangesRef.current = fetchedRanges
  const inFlightRef = useRef<Set<string>>(new Set())

  const recordingBase = byInvite
    ? `${serverUrl}/session-invites/${inviteCode}`
    : sessionId
    ? `${serverUrl}/sessions/${sessionId}`
    : null
  const authHeaders = byInvite ? {} : { 'Authorization': `Bearer ${accessToken}` }

  // Reset all cached state when switching sessions/invites.
  useEffect(() => {
    setByRunner(new Map())
    setFetchedRanges([])
    setRunStartMs(null)
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

    fetch(`${recordingBase}/recording/meta`, { headers: authHeaders })
      .then(res => {
        if (cancelled) return null
        if (res.status === 404) return null
        if (!res.ok) throw new Error(`Failed to load recording metadata: HTTP ${res.status}`)
        return res.json()
      })
      .then(meta => {
        if (cancelled || !meta?.runStartTimestamp) return
        setRunStartMs(new Date(meta.runStartTimestamp).getTime())
      })
      .catch(err => {
        if (!cancelled) console.error('[useSessionTimeline] meta fetch failed', err)
      })

    return () => { cancelled = true }
  }, [active, recordingBase]) // eslint-disable-line react-hooks/exhaustive-deps

  // Live-follow polling — only runs while following (scrubTimeMs === null).
  useEffect(() => {
    if (!active || scrubTimeMs !== null) return
    let cancelled = false
    let stopped = false
    let id: ReturnType<typeof setInterval> | undefined

    async function fetchAndUpdate() {
      try {
        const res = byInvite
          ? await fetch(`${serverUrl}/session-invites/${inviteCode}/locations`)
          : await fetch(`${serverUrl}/locations/${sessionId}`, { headers: authHeaders })
        if (cancelled) return
        if (!res.ok) {
          setError(livePollingError(res.status))
          if (byInvite && res.status === 404) {
            stopped = true
            setInvalidInvite(true)
            clearInterval(id)
          }
          return
        }
        const runnerGroups: RunnerPosition[][] = await res.json()
        if (cancelled) return
        setError(null)
        setNowMs(Date.now())
        setByRunner(prev => mergeIntoByRunner(prev, runnerGroups.flat()))
      } catch {
        if (!cancelled) setError('Network error while loading live positions.')
      }
    }

    fetchAndUpdate().then(() => {
      if (!cancelled && !stopped) id = setInterval(fetchAndUpdate, pollIntervalMs)
    })
    return () => {
      cancelled = true
      clearInterval(id)
    }
  }, [active, scrubTimeMs, sessionId, serverUrl, accessToken, pollIntervalMs, byInvite, inviteCode]) // eslint-disable-line react-hooks/exhaustive-deps

  function fetchWindow(sinceMs: number, untilMs: number) {
    if (!recordingBase) return
    const key = `${sinceMs}:${untilMs}`
    if (inFlightRef.current.has(key)) return
    inFlightRef.current.add(key)
    setLoading(true)

    const since = new Date(sinceMs).toISOString()
    const until = new Date(untilMs).toISOString()

    fetch(`${recordingBase}/recording?since=${encodeURIComponent(since)}&until=${encodeURIComponent(until)}`, {
      headers: authHeaders,
    })
      .then(async res => {
        if (!res.ok) throw new Error(`Failed to load recording window: HTTP ${res.status}`)
        const text = await res.text()
        const updates = parseNdjson(text)
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
    const floor = runStartMs ?? targetMs
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
    setScrubTimeMs(Math.max(runStartMs ?? ms, Math.min(ms, nowMs)))
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
  useEffect(() => {
    if (!playing) return
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
  }, [playing, nowMs]) // eslint-disable-line react-hooks/exhaustive-deps

  const positions = useMemo((): RunnerPosition[][] | undefined => {
    const cutoff = scrubTimeMs ?? nowMs
    const result = positionsAtCutoff(byRunner, cutoff)
    return result.length > 0 ? result : undefined
  }, [byRunner, scrubTimeMs, nowMs])

  return {
    positions,
    following: scrubTimeMs === null,
    scrubTimeMs,
    runStartMs,
    nowMs,
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
