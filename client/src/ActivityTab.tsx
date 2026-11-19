import { useEffect, useState } from 'react'
import { apiHeaders } from './apiHeaders'
import { initialsFor } from './components/InitialsBadge.tsx'
import {
  buildFlow,
  dayRange,
  todayString,
  type ActivityDotDto,
  type ActivityEventDto,
  type Flow,
  type RegisterEntryDto,
} from './activityFlow.ts'

// The admin Activity tab: what each dot did on a day, as one line per dot with evenly spaced stops and
// branches for what someone else did to them (server: Controllers/ActivityController.cs). See
// .ai/plans/activity-flow.md.

// The busiest few dots are shown when a day loads; the rest can be ticked.
const DEFAULT_SELECTED = 5

type Load<T> = { kind: 'idle' } | { kind: 'loading' } | { kind: 'ok'; value: T } | { kind: 'error'; message: string }

export default function ActivityTab({ serverUrl, token }: { serverUrl: string; token: string }) {
  const [day, setDay] = useState(todayString)
  const [register, setRegister] = useState<RegisterEntryDto[]>([])
  const [registerUnavailable, setRegisterUnavailable] = useState(false)
  const [dots, setDots] = useState<Load<ActivityDotDto[]>>({ kind: 'idle' })
  const [selected, setSelected] = useState<string[]>([])
  const [events, setEvents] = useState<Load<ActivityEventDto[]>>({ kind: 'idle' })
  const [refreshTick, setRefreshTick] = useState(0)

  const headers = apiHeaders(token, 'web-admin')

  useEffect(() => {
    let cancelled = false
    fetch(`${serverUrl}/admin/activity/register`, { headers })
      .then(async response => {
        if (cancelled) return
        if (!response.ok) { setRegisterUnavailable(true); return }
        setRegister(await response.json() as RegisterEntryDto[])
      })
      .catch(() => { if (!cancelled) setRegisterUnavailable(true) })
    return () => { cancelled = true }
  }, [serverUrl, token])

  // The dots with activity that day; the busiest are ticked.
  useEffect(() => {
    let cancelled = false
    setDots({ kind: 'loading' })
    const { from, to } = dayRange(day)
    fetch(`${serverUrl}/admin/activity/dots?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}`, { headers })
      .then(async response => {
        if (cancelled) return
        if (!response.ok) { setDots({ kind: 'error', message: `Couldn't load the day (${response.status}).` }); return }
        const value = await response.json() as ActivityDotDto[]
        setDots({ kind: 'ok', value })
        setSelected(value.slice(0, DEFAULT_SELECTED).map(d => d.userId))
      })
      .catch(() => { if (!cancelled) setDots({ kind: 'error', message: "Couldn't reach the server." }) })
    return () => { cancelled = true }
  }, [serverUrl, token, day, refreshTick])

  // The ticked dots' events.
  useEffect(() => {
    if (selected.length === 0) { setEvents({ kind: 'ok', value: [] }); return }
    let cancelled = false
    setEvents({ kind: 'loading' })
    const { from, to } = dayRange(day)
    const query = `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&userIds=${selected.map(encodeURIComponent).join(',')}`
    fetch(`${serverUrl}/admin/activity?${query}`, { headers })
      .then(async response => {
        if (cancelled) return
        if (!response.ok) { setEvents({ kind: 'error', message: `Couldn't load the events (${response.status}).` }); return }
        setEvents({ kind: 'ok', value: await response.json() as ActivityEventDto[] })
      })
      .catch(() => { if (!cancelled) setEvents({ kind: 'error', message: "Couldn't reach the server." }) })
    return () => { cancelled = true }
  }, [serverUrl, token, day, selected])

  function toggle(userId: string) {
    setSelected(current => current.includes(userId) ? current.filter(id => id !== userId) : [...current, userId])
  }

  const dotList = dots.kind === 'ok' ? dots.value : []
  const lines = dotList
    .filter(dot => selected.includes(dot.userId))
    .map(dot => ({
      dot,
      flow: buildFlow(events.kind === 'ok' ? events.value.filter(e => e.userId === dot.userId) : [], register),
    }))

  if (registerUnavailable) {
    return (
      <>
        <h2 style={heading}>Activity</h2>
        <p style={note}>This server doesn't record activity yet.</p>
      </>
    )
  }

  return (
    <>
      <h2 style={heading}>Activity</h2>
      <p style={note}>
        What each dot did, in order. Stops are evenly spaced, not to time scale. Below the line: someone else,
        such as a person watching a Go run. Events are kept for 30 days.
      </p>

      <div style={controls}>
        <label style={fieldLabel}>
          Day
          <input type="date" value={day} max={todayString()} onChange={e => e.target.value && setDay(e.target.value)} style={dateInput} />
        </label>
        <button style={refreshBtn} onClick={() => setRefreshTick(t => t + 1)}>Refresh</button>
      </div>

      {dots.kind === 'loading' && <p style={note}>Loading…</p>}
      {dots.kind === 'error' && <p style={errorText}>{dots.message}</p>}
      {dots.kind === 'ok' && dotList.length === 0 && <p style={note}>No activity on this day.</p>}

      {dotList.length > 0 && (
        <div style={dotPicker} role="group" aria-label="Dots to show">
          {dotList.map(dot => (
            <label key={dot.userId} style={dotChip(selected.includes(dot.userId))}>
              <input type="checkbox" checked={selected.includes(dot.userId)} onChange={() => toggle(dot.userId)} style={{ margin: 0 }} />
              <span style={chipBadge}>{initialsFor(dot.displayName)}</span>
              {dot.displayName} <span style={{ color: '#64748b' }}>({dot.eventCount})</span>
            </label>
          ))}
        </div>
      )}

      {events.kind === 'error' && <p style={errorText}>{events.message}</p>}
      {lines.map(({ dot, flow }) => <DotLine key={dot.userId} dot={dot} flow={flow} />)}

      <RegisterList register={register} />
    </>
  )
}

// ---- One dot's line ----------------------------------------------------------------------------

const SPACING = 170
const START_X = 24
const LINE_Y = 40
const BRANCH_TOP = LINE_Y + 96
const BRANCH_HEIGHT = 52
const BRANCH_GAP = 12
const COLOURS = { line: '#334155', run: '#ea580c', branch: '#0e7490' }

function formatTime(at: string) {
  return new Date(at).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

function DotLine({ dot, flow }: { dot: ActivityDotDto; flow: Flow }) {
  const x = (index: number) => START_X + index * SPACING
  const lastIndex = Math.max(flow.stops.length - 1, 0)
  const width = x(lastIndex) + SPACING
  const branchesPerStop = new Map<number, number>()
  const placedBranches = flow.branches.map(branch => {
    const slot = branchesPerStop.get(branch.fromIndex) ?? 0
    branchesPerStop.set(branch.fromIndex, slot + 1)
    return { branch, top: BRANCH_TOP + slot * (BRANCH_HEIGHT + BRANCH_GAP) }
  })
  const height = Math.max(LINE_Y + 72, ...placedBranches.map(p => p.top + BRANCH_HEIGHT + 8))

  return (
    <section style={lineCard} aria-label={`${dot.displayName}'s activity`}>
      <div style={lineHeader}>
        <span style={lineBadge}>{initialsFor(dot.displayName)}</span>
        <span style={{ fontWeight: 600 }}>{dot.displayName}</span>
        <span style={{ color: '#64748b', fontSize: '0.8rem' }}>{dot.username}</span>
      </div>
      {flow.stops.length === 0 ? (
        <p style={{ ...note, margin: '0.5rem 0 0' }}>Nothing recorded.</p>
      ) : (
        <div style={{ overflowX: 'auto' }}>
          <div style={{ position: 'relative', width, height }}>
            {/* The line, then the stretches where a Go run was going over it. */}
            <div style={{ ...rail, left: x(0), width: Math.max(x(lastIndex) - x(0), 0), background: COLOURS.line }} />
            {flow.runs.map((run, i) => {
              const left = x(run.fromIndex)
              const right = run.toIndex === null ? x(lastIndex) + SPACING / 2 : x(run.toIndex)
              return <div key={i} style={{ ...rail, height: 8, top: LINE_Y - 4, left, width: Math.max(right - left, 8), background: COLOURS.run }} />
            })}

            {placedBranches.map(({ branch, top }) => (
              <div key={`stem-${branch.fromIndex}-${branch.kind}`}
                style={{ position: 'absolute', left: x(branch.fromIndex) - 1, top: LINE_Y, width: 2, height: top - LINE_Y, background: COLOURS.branch }} />
            ))}

            {flow.stops.map(stop => {
              const inRun = flow.runs.some(r => stop.index >= r.fromIndex && (r.toIndex === null || stop.index <= r.toIndex))
              return (
                <div key={stop.index}>
                  <div style={{ ...stopDot, left: x(stop.index) - 9, borderColor: inRun ? COLOURS.run : COLOURS.line }} />
                  <div style={{ ...stopLabel, left: x(stop.index) + 12 }}>
                    <div style={{ fontWeight: 600 }}>{stop.label}</div>
                    <div style={{ color: '#64748b', fontSize: '0.75rem' }}>
                      {formatTime(stop.at)}{stop.note ? ` · ${stop.note}` : ''}
                    </div>
                  </div>
                </div>
              )
            })}

            {placedBranches.map(({ branch, top }) => (
              <div key={`card-${branch.fromIndex}-${branch.kind}`} style={{ ...branchCard, left: x(branch.fromIndex) - 20, top }}>
                <div style={{ fontWeight: 600 }}>{branch.label}{branch.count > 1 ? ` ×${branch.count}` : ''}</div>
                <div style={{ color: '#475569', fontSize: '0.75rem' }}>{branch.count > 1 ? 'first ' : ''}{formatTime(branch.firstAt)}</div>
              </div>
            ))}
          </div>
        </div>
      )}
    </section>
  )
}

// ---- The register --------------------------------------------------------------------------------

function RegisterList({ register }: { register: RegisterEntryDto[] }) {
  if (register.length === 0) return null
  const sorted = [...register].sort((a, b) => (a.status === b.status ? 0 : a.status === 'recording' ? -1 : 1))
  return (
    <details style={{ marginTop: '1.5rem' }}>
      <summary style={{ cursor: 'pointer', fontWeight: 600 }}>
        Register: {register.filter(e => e.status === 'recording').length} recorded, {register.filter(e => e.status === 'wanted').length} wanted
      </summary>
      <p style={note}>From server/Activity/activity-events.json. To ask for an event, add it there as "wanted".</p>
      <div style={{ overflowX: 'auto' }}>
        <table style={registerTable}>
          <thead>
            <tr>
              <th style={registerTh}>Label</th>
              <th style={registerTh}>Kind</th>
              <th style={registerTh}>Shows</th>
              <th style={registerTh}>Route</th>
              <th style={registerTh}>Status</th>
            </tr>
          </thead>
          <tbody>
            {sorted.map(entry => (
              <tr key={entry.kind} style={{ borderBottom: '1px solid #f1f5f9' }}>
                <td style={registerTd}>{entry.label}</td>
                <td style={{ ...registerTd, fontFamily: 'monospace' }}>{entry.kind}</td>
                <td style={registerTd}>{entry.shows === 'branch' ? `branch from ${entry.branchFrom ?? '?'}` : 'line'}</td>
                <td style={{ ...registerTd, fontFamily: 'monospace' }}>{entry.route ?? entry.describe ?? ''}</td>
                <td style={registerTd}>
                  <span style={statusPill(entry.status)}>{entry.status}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </details>
  )
}

// ---- Styles ----------------------------------------------------------------------------------------

const heading: React.CSSProperties = { margin: '0 0 0.25rem', fontSize: '1.1rem' }
const note: React.CSSProperties = { color: '#64748b', fontSize: '0.85rem', margin: '0 0 1rem' }
const errorText: React.CSSProperties = { color: '#b91c1c', fontSize: '0.85rem' }
const controls: React.CSSProperties = { display: 'flex', flexWrap: 'wrap', gap: '0.75rem', alignItems: 'flex-end', marginBottom: '1rem' }
const fieldLabel: React.CSSProperties = { display: 'flex', flexDirection: 'column', gap: 4, fontSize: '0.8rem', color: '#64748b' }
const dateInput: React.CSSProperties = { font: 'inherit', fontSize: '0.9rem', padding: '0.35rem 0.5rem', border: '1px solid #cbd5e1', borderRadius: 6, color: '#0f172a' }
const refreshBtn: React.CSSProperties = { font: 'inherit', fontSize: '0.85rem', padding: '0.4rem 0.8rem', border: '1px solid #cbd5e1', borderRadius: 6, background: '#fff', cursor: 'pointer' }
const dotPicker: React.CSSProperties = { display: 'flex', flexWrap: 'wrap', gap: '0.5rem', marginBottom: '1rem' }
const dotChip = (on: boolean): React.CSSProperties => ({
  display: 'inline-flex', alignItems: 'center', gap: 6, padding: '0.3rem 0.6rem', borderRadius: 999,
  border: `1px solid ${on ? '#1f6feb' : '#cbd5e1'}`, background: on ? '#eff6ff' : '#fff', fontSize: '0.85rem', cursor: 'pointer',
})
const chipBadge: React.CSSProperties = {
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', width: 22, height: 22, borderRadius: '50%',
  background: '#1f6feb', color: '#fff', fontSize: '0.65rem', fontWeight: 700,
}
const lineCard: React.CSSProperties = { background: '#fff', border: '1px solid #e2e8f0', borderRadius: 10, padding: '0.75rem 1rem', marginBottom: '0.75rem' }
const lineHeader: React.CSSProperties = { display: 'flex', alignItems: 'center', gap: 8, marginBottom: '0.25rem' }
const lineBadge: React.CSSProperties = { ...chipBadge, width: 28, height: 28, fontSize: '0.75rem' }
const rail: React.CSSProperties = { position: 'absolute', top: LINE_Y - 2, height: 4, borderRadius: 4 }
const stopDot: React.CSSProperties = {
  position: 'absolute', top: LINE_Y - 9, width: 18, height: 18, boxSizing: 'border-box', borderRadius: '50%',
  background: '#fff', border: '4px solid', zIndex: 1,
}
const stopLabel: React.CSSProperties = { position: 'absolute', top: LINE_Y + 12, width: SPACING - 24, fontSize: '0.85rem', lineHeight: 1.3 }
const branchCard: React.CSSProperties = {
  position: 'absolute', width: SPACING - 20, height: BRANCH_HEIGHT, boxSizing: 'border-box', padding: '0.4rem 0.6rem',
  background: '#ecfeff', border: `1.5px solid ${COLOURS.branch}`, borderRadius: 8, fontSize: '0.8rem', lineHeight: 1.3,
}
const registerTable: React.CSSProperties = { width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }
const registerTh: React.CSSProperties = { textAlign: 'left', padding: '0.4rem 0.6rem', borderBottom: '2px solid #e2e8f0', color: '#64748b', fontWeight: 600 }
const registerTd: React.CSSProperties = { padding: '0.35rem 0.6rem', verticalAlign: 'top' }
const statusPill = (status: string): React.CSSProperties => ({
  display: 'inline-block', padding: '0.1rem 0.5rem', borderRadius: 999, fontSize: '0.75rem', fontWeight: 600,
  background: status === 'recording' ? '#dcfce7' : '#fef9c3', color: status === 'recording' ? '#166534' : '#854d0e',
})
