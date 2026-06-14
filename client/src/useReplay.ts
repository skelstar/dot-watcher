import { useEffect, useMemo, useRef, useState } from 'react'
import type { RunnerPosition } from './types.ts'

interface LocationUpdate extends RunnerPosition {
  sessionCode: string
}

// Normalises both camelCase (future server output) and PascalCase (legacy NDJSON).
// eslint-disable-next-line @typescript-eslint/no-explicit-any
function normalizeUpdate(obj: any): LocationUpdate {
  return {
    runnerName:  obj.runnerName  ?? obj.RunnerName,
    sessionCode: obj.sessionCode ?? obj.SessionCode,
    latitude:    obj.latitude    ?? obj.Latitude,
    longitude:   obj.longitude   ?? obj.Longitude,
    heading:     obj.heading     ?? obj.Heading     ?? null,
    timestamp:   obj.timestamp   ?? obj.Timestamp,
  }
}

export interface ReplayState {
  positions: RunnerPosition[][] | undefined
  currentTimeMs: number
  durationMs: number
  loaded: boolean
  error: string | null
  playing: boolean
  speed: number
  virtualNowMs: number
  play: () => void
  pause: () => void
  seek: (ms: number) => void
  setSpeed: (s: number) => void
}

const TRAIL_LENGTH = 1
const TICK_MS = 100

export function useReplay(sessionCode: string | null, serverUrl: string): ReplayState {
  const [byRunner, setByRunner] = useState<Map<string, LocationUpdate[]> | null>(null)
  const [startEpochMs, setStartEpochMs] = useState(0)
  const [durationMs, setDurationMs] = useState(0)
  const [currentTimeMs, setCurrentTimeMs] = useState(0)
  const [playing, setPlaying] = useState(false)
  const [speed, setSpeed] = useState(10)
  const [error, setError] = useState<string | null>(null)
  const speedRef = useRef(speed)
  speedRef.current = speed
  const playingRef = useRef(false)

  useEffect(() => {
    if (!sessionCode) return
    setByRunner(null)
    setCurrentTimeMs(0)
    setPlaying(false)
    setError(null)

    fetch(`${serverUrl}/sessions/${sessionCode}/recording`)
      .then(r => {
        if (!r.ok) throw new Error(`HTTP ${r.status} for ${sessionCode}`)
        return r.text()
      })
      .then(text => {
        const updates: LocationUpdate[] = text
          .split('\n')
          .filter(Boolean)
          .map(line => normalizeUpdate(JSON.parse(line)))

        const map = new Map<string, LocationUpdate[]>()
        for (const u of updates) {
          if (!map.has(u.runnerName)) map.set(u.runnerName, [])
          map.get(u.runnerName)!.push(u)
        }
        for (const arr of map.values()) {
          arr.sort((a, b) => new Date(a.timestamp).getTime() - new Date(b.timestamp).getTime())
        }

        const allTs = updates.map(u => new Date(u.timestamp).getTime())
        const minT = Math.min(...allTs)
        const maxT = Math.max(...allTs)

        setStartEpochMs(minT)
        setDurationMs(maxT - minT)
        setByRunner(map)
      })
      .catch(err => {
        console.error('[useReplay]', err)
        setError(err instanceof Error ? err.message : 'Failed to load recording')
      })
  }, [sessionCode, serverUrl])

  useEffect(() => {
    if (!playing) return
    playingRef.current = true
    const id = setInterval(() => {
      if (!playingRef.current) return
      setCurrentTimeMs(t => {
        const next = t + TICK_MS * speedRef.current
        if (next >= durationMs) {
          playingRef.current = false
          setPlaying(false)
          return durationMs
        }
        return next
      })
    }, TICK_MS)
    return () => { playingRef.current = false; clearInterval(id) }
  }, [playing, durationMs])

  const positions = useMemo((): RunnerPosition[][] | undefined => {
    if (!byRunner) return undefined
    const cutoff = startEpochMs + currentTimeMs
    const result: RunnerPosition[][] = []
    for (const arr of byRunner.values()) {
      const upTo = arr.filter(p => new Date(p.timestamp).getTime() <= cutoff)
      if (upTo.length === 0) continue
      result.push(upTo.slice(-TRAIL_LENGTH))
    }
    return result.length > 0 ? result : undefined
  }, [byRunner, startEpochMs, currentTimeMs])

  return {
    positions,
    currentTimeMs,
    durationMs,
    loaded: byRunner !== null,
    error,
    playing,
    speed,
    virtualNowMs: startEpochMs + currentTimeMs,
    play: () => { if (currentTimeMs >= durationMs) setCurrentTimeMs(0); setPlaying(true) },
    pause: () => { playingRef.current = false; setPlaying(false) },
    seek: (ms) => setCurrentTimeMs(Math.max(0, Math.min(ms, durationMs))),
    setSpeed,
  }
}
