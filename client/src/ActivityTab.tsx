import { useEffect, useRef, useState } from 'react'
import { apiHeaders } from './apiHeaders'
import { initialsFor } from './components/InitialsBadge.tsx'
import {
  LAYOUT,
  buildFlow,
  dayRange,
  formatDuration,
  MAX_TIME_WIDTH,
  layoutFlow,
  readableWidth,
  timeBreaks,
  timeScale,
  timeScaleOfWidth,
  timeTicks,
  todayString,
  type ActivityDotDto,
  type ActivityEventDto,
  type Flow,
  type FlowLayout,
  type RegisterEntryDto,
  type TimeScale,
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
  const [view, setView] = useState<'order' | 'time'>('order')
  const [zoom, setZoom] = useState<'fit' | number>(1)
  // By time: long stretches where no line has anything shrink to a narrow break.
  const [collapseQuiet, setCollapseQuiet] = useState(true)
  // Testing on the live server: activity an admin marked as test is left out unless this is off.
  const [hideTest, setHideTest] = useState(true)
  const [markError, setMarkError] = useState<string | null>(null)
  // How wide the lines can be drawn, so 1× by time fits the panel.
  const [panelWidth, setPanelWidth] = useState(900)
  const panelRef = useRef<HTMLDivElement>(null)
  // By time, the lines scroll together: each line's scroller, and the one being scrolled by hand.
  const scrollers = useRef(new Set<HTMLDivElement>())

  const headers = apiHeaders(token, 'web-admin')

  useEffect(() => {
    const panel = panelRef.current
    if (!panel) return
    const observer = new ResizeObserver(entries => setPanelWidth(entries[0].contentRect.width))
    observer.observe(panel)
    return () => observer.disconnect()
  }, [])

  // The others follow the one scrolled. Their own scroll events then find everyone already in step, so
  // nothing bounces back.
  function syncScroll(source: HTMLDivElement) {
    if (view !== 'time') return
    for (const other of scrollers.current)
      if (other !== source && other.scrollLeft !== source.scrollLeft) other.scrollLeft = source.scrollLeft
  }

  function registerScroller(el: HTMLDivElement | null) {
    if (!el) return
    scrollers.current.add(el)
    return () => { scrollers.current.delete(el) }
  }

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
    fetch(`${serverUrl}/admin/activity/dots?from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}&includeTest=${!hideTest}`, { headers })
      .then(async response => {
        if (cancelled) return
        if (!response.ok) { setDots({ kind: 'error', message: `Couldn't load the day (${response.status}).` }); return }
        const value = await response.json() as ActivityDotDto[]
        setDots({ kind: 'ok', value })
        // Keep the ticked dots that are still there (a refresh, a mark); otherwise the busiest.
        setSelected(current => {
          const kept = current.filter(id => value.some(d => d.userId === id))
          return kept.length > 0 ? kept : value.slice(0, DEFAULT_SELECTED).map(d => d.userId)
        })
      })
      .catch(() => { if (!cancelled) setDots({ kind: 'error', message: "Couldn't reach the server." }) })
    return () => { cancelled = true }
  }, [serverUrl, token, day, refreshTick, hideTest])

  // The ticked dots' events.
  useEffect(() => {
    if (selected.length === 0) { setEvents({ kind: 'ok', value: [] }); return }
    let cancelled = false
    setEvents({ kind: 'loading' })
    const { from, to } = dayRange(day)
    const query = `from=${encodeURIComponent(from)}&to=${encodeURIComponent(to)}` +
      `&userIds=${selected.map(encodeURIComponent).join(',')}&includeTest=${!hideTest}`
    fetch(`${serverUrl}/admin/activity?${query}`, { headers })
      .then(async response => {
        if (cancelled) return
        if (!response.ok) { setEvents({ kind: 'error', message: `Couldn't load the events (${response.status}).` }); return }
        setEvents({ kind: 'ok', value: await response.json() as ActivityEventDto[] })
      })
      .catch(() => { if (!cancelled) setEvents({ kind: 'error', message: "Couldn't reach the server." }) })
    return () => { cancelled = true }
  }, [serverUrl, token, day, selected, hideTest])

  // Marks a Go run on one dot's line, and what happened during it, as test (or back to real), then reloads.
  async function markTest(userId: string, runId: string, isTest: boolean) {
    setMarkError(null)
    const body = { userId, runId, isTest }
    try {
      const response = await fetch(`${serverUrl}/admin/activity/test`, {
        method: 'POST',
        headers: { ...headers, 'Content-Type': 'application/json' },
        body: JSON.stringify(body),
      })
      if (!response.ok) { setMarkError(`Couldn't mark it (${response.status}).`); return }
      setRefreshTick(t => t + 1)
    } catch {
      setMarkError("Couldn't reach the server.")
    }
  }

  function toggle(userId: string) {
    setSelected(current => current.includes(userId) ? current.filter(id => id !== userId) : [...current, userId])
  }

  const dotList = dots.kind === 'ok' ? dots.value : []
  const shownEvents = events.kind === 'ok' ? events.value.filter(e => selected.includes(e.userId)) : []
  // By time, every line shares one scale, so the same moment is at the same place on each. Fit puts the
  // whole day in the panel; 1× spaces stops about as In order does, and the lines scroll together.
  const fitWidth = Math.max(300, panelWidth - LINE_CARD_INSET - LAYOUT.startX - LAYOUT.labelWidth - 28)
  // With collapsing, quiet stretches shrink to breaks, so the cap applies to the width that's drawn.
  const scale = view !== 'time' ? null : (() => {
    if (zoom === 'fit') return timeScaleOfWidth(shownEvents, fitWidth, collapseQuiet)
    const straight = (readableWidth(shownEvents, collapseQuiet ? Infinity : MAX_TIME_WIDTH) ?? fitWidth) * zoom
    const drawn = timeScale(shownEvents, straight, collapseQuiet)
    if (!drawn || drawn.width < fitWidth) return timeScaleOfWidth(shownEvents, fitWidth, collapseQuiet)
    return drawn.width > MAX_TIME_WIDTH ? timeScaleOfWidth(shownEvents, MAX_TIME_WIDTH, collapseQuiet) : drawn
  })()
  const laidOut = dotList
    .filter(dot => selected.includes(dot.userId))
    .map(dot => {
      const flow = buildFlow(shownEvents.filter(e => e.userId === dot.userId), register)
      return { dot, flow, layout: layoutFlow(flow, scale) }
    })
  // By time, every line is as wide as the widest, so they scroll as one.
  const sharedWidth = scale ? Math.max(0, ...laidOut.map(l => l.layout.width)) : null

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
        What each dot did. In order: stops evenly spaced, as a story. By time: every line on one clock, so you
        can see what happened when, relative to the others; the lines scroll together. Orange: a Go run. Dashed:
        the app was in the background. Below the line: someone else, such as a person watching a Go run, with
        how long they had it on screen (by time, a bar under the line for each viewing).
        Events are kept for 30 days.
      </p>

      <div style={controls}>
        <label style={fieldLabel}>
          Day
          <input type="date" value={day} max={todayString()} onChange={e => e.target.value && setDay(e.target.value)} style={dateInput} />
        </label>
        <div style={fieldLabel}>
          Show
          <div style={segmented} role="group" aria-label="How to space the stops">
            <button style={segment(view === 'order')} aria-pressed={view === 'order'} onClick={() => setView('order')}>In order</button>
            <button style={segment(view === 'time')} aria-pressed={view === 'time'} onClick={() => setView('time')}>By time</button>
          </div>
        </div>
        {view === 'time' && (
          <label style={fieldLabel}>
            Zoom
            <select value={String(zoom)} onChange={e => setZoom(e.target.value === 'fit' ? 'fit' : Number(e.target.value))} style={dateInput}
              title="Fit: the whole day in the panel. 1×: stops spaced about as In order, scrolling.">
              <option value="fit">Fit</option>
              {ZOOMS.map(z => <option key={z} value={z}>{z}×</option>)}
            </select>
          </label>
        )}
        {view === 'time' && (
          <label style={{ ...fieldLabel, flexDirection: 'row', alignItems: 'center', alignSelf: 'center', color: '#0f172a' }}
            title="Long stretches where no line has anything are shrunk to a break marked with how long they were">
            <input type="checkbox" checked={collapseQuiet} onChange={e => setCollapseQuiet(e.target.checked)} style={{ margin: 0 }} />
            Collapse quiet stretches
          </label>
        )}
        <label style={{ ...fieldLabel, flexDirection: 'row', alignItems: 'center', alignSelf: 'center', color: '#0f172a' }}
          title="Runs and stretches marked as test (testing on the live server) are left out">
          <input type="checkbox" checked={hideTest} onChange={e => setHideTest(e.target.checked)} style={{ margin: 0 }} />
          Hide test activity
        </label>
        <button style={refreshBtn} onClick={() => setRefreshTick(t => t + 1)}>Refresh</button>
      </div>
      <p style={note}>
        Testing? Mark a Go run as test with its button on the line.
        Nothing is deleted; untick Hide test activity to see it and mark it back.
      </p>
      {markError && <p style={errorText}>{markError}</p>}

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
      <div ref={panelRef}>
        {laidOut.map(({ dot, flow, layout }) => (
          <DotLine
            key={dot.userId}
            dot={dot}
            flow={flow}
            layout={layout}
            scale={scale}
            width={sharedWidth ?? layout.width}
            scrollerRef={registerScroller}
            onScroll={syncScroll}
            onMark={(runId, isTest) => markTest(dot.userId, runId, isTest)}
          />
        ))}
      </div>

      <RegisterList register={register} />
    </>
  )
}

// ---- One dot's line ----------------------------------------------------------------------------

// Times the readable spacing (1×); "Fit" is separate.
const ZOOMS = [1, 2, 4]
// A line card's horizontal padding and border, both sides.
const LINE_CARD_INSET = 34
const LABEL_ROW = 42
const BRANCH_HEIGHT = 52
const BRANCH_GAP = 12
const COLOURS = { line: '#334155', run: '#ea580c', branch: '#0e7490', grid: '#e2e8f0' }

function formatTime(at: string | number, withSeconds = true) {
  return new Date(at).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', ...(withSeconds ? { second: '2-digit' } : {}) })
}

function DotLine({ dot, flow, layout, scale, width, scrollerRef, onScroll, onMark }: {
  dot: ActivityDotDto
  flow: Flow
  layout: FlowLayout
  scale: TimeScale | null
  width: number
  scrollerRef: (el: HTMLDivElement | null) => (() => void) | undefined
  onScroll: (el: HTMLDivElement) => void
  onMark: (runId: string, isTest: boolean) => void
}) {
  // The Go runs on this line, for their mark buttons. A run is test when its start (or stop) is.
  const runs = flow.runs
    .filter(run => run.runId)
    .map(run => {
      const first = flow.stops[run.fromIndex]
      const last = run.toIndex === null ? null : flow.stops[run.toIndex]
      return { runId: run.runId!, first, last, isTest: first.isTest || (last?.isTest ?? false) }
    })
  // By time, gridline labels sit above the line.
  const lineY = scale ? 52 : 40
  // By time, a strip just under the line holds the viewing bars, so labels start a little lower.
  const spanY = lineY + 13
  const labelTop = (row: number) => lineY + (scale ? 22 : 12) + row * LABEL_ROW
  const labelsBottom = labelTop(layout.labelRows)
  const branchTop = (row: number) => labelsBottom + 16 + row * (BRANCH_HEIGHT + BRANCH_GAP)
  const height = layout.branchRows > 0 ? branchTop(layout.branchRows - 1) + BRANCH_HEIGHT + 8 : labelsBottom + 4
  const ticks = scale ? timeTicks(scale) : []
  const breaks = scale ? timeBreaks(scale) : []
  const rail: React.CSSProperties = { position: 'absolute', top: lineY - 2, height: 4, borderRadius: 4 }

  return (
    <section style={lineCard} aria-label={`${dot.displayName}'s activity`}>
      <div style={lineHeader}>
        <span style={lineBadge}>{initialsFor(dot.displayName)}</span>
        <span style={{ fontWeight: 600 }}>{dot.displayName}</span>
        <span style={{ color: '#64748b', fontSize: '0.8rem' }}>{dot.username}</span>
        <span style={{ flex: 1 }} />
        {runs.map(run => (
          <button key={run.runId} style={markBtn(run.isTest)} onClick={() => onMark(run.runId, !run.isTest)}
            title={run.isTest ? 'Count this Go run as real again' : 'Mark this Go run, and what happened during it, as test'}>
            {run.isTest ? 'Not test: ' : 'Test: '}Go run {formatTime(run.first.at, false)}{run.last ? `–${formatTime(run.last.at, false)}` : ''}
          </button>
        ))}
      </div>
      {flow.stops.length === 0 ? (
        <p style={{ ...note, margin: '0.5rem 0 0' }}>Nothing recorded.</p>
      ) : (
        <div style={{ overflowX: 'auto' }} ref={scrollerRef} onScroll={e => onScroll(e.currentTarget)}>
          <div style={{ position: 'relative', width, height }}>
            {/* By time: gridlines first, so everything else draws over them. */}
            {ticks.map(tick => (
              <div key={tick.at}>
                <div style={{ position: 'absolute', left: tick.x, top: 18, bottom: 0, width: 1, background: COLOURS.grid }} />
                <div style={{ ...tickLabel, left: tick.x }}>{formatTime(tick.at, tick.withSeconds)}</div>
              </div>
            ))}

            {/* The line, then the stretches where a Go run was going over it. */}
            <div style={{ ...rail, left: layout.lineLeft, width: Math.max(layout.lineRight - layout.lineLeft, 0), background: COLOURS.line }} />
            {layout.runs.map((run, i) => (
              <div key={i} style={{ ...rail, height: 8, top: lineY - 4, left: run.left, width: Math.max(run.right - run.left, 8), background: COLOURS.run }} />
            ))}
            {/* While the app was in the background: white gaps cut the line (and any run) into dashes. */}
            {layout.away.map((span, i) => (
              <div key={`away-${i}`} title="App in the background" style={{
                position: 'absolute', top: lineY - 5, height: 10, left: span.left + 10, width: Math.max(span.right - span.left - 20, 0),
                background: 'repeating-linear-gradient(to right, transparent 0 10px, #fff 10px 16px)',
              }} />
            ))}

            {/* By time, collapsed: a quiet stretch (nothing on any line) shrunk to a break between two dashed
                lines, cutting the line, with how long it was. */}
            {breaks.map(gap => (
              <div key={`break-${gap.left}`}>
                <div title={`Nothing happened for ${formatDuration(Math.round(gap.quietMs / 60_000) * 60)}`} style={{
                  position: 'absolute', top: 18, bottom: 0, left: gap.left, width: gap.right - gap.left, boxSizing: 'border-box',
                  background: '#fff', borderLeft: '2px dashed #94a3b8', borderRight: '2px dashed #94a3b8',
                }} />
                <div style={{ ...tickLabel, left: (gap.left + gap.right) / 2, fontWeight: 600 }}>
                  {formatDuration(Math.round(gap.quietMs / 60_000) * 60)}
                </div>
              </div>
            ))}

            {/* By time: when each viewing was on screen, under the line. */}
            {layout.branches.flatMap(({ branch, spans }) => spans.map((span, i) => (
              <div key={`span-${branch.fromIndex}-${branch.kind}-${i}`}
                title={span.ongoing ? 'Watching (length not known yet)' : 'Watching'}
                style={{
                  position: 'absolute', top: spanY, height: 4, borderRadius: 2, left: span.left, width: span.right - span.left,
                  background: span.ongoing ? `linear-gradient(to right, ${COLOURS.branch}, transparent)` : COLOURS.branch,
                }} />
            )))}

            {layout.branches.map(({ branch, x, row }) => (
              <div key={`stem-${branch.fromIndex}-${branch.kind}`}
                style={{ position: 'absolute', left: x - 1, top: lineY, width: 2, height: branchTop(row) - lineY, background: COLOURS.branch }} />
            ))}

            {flow.stops.map(stop => {
              const inRun = flow.runs.some(r => stop.index >= r.fromIndex && (r.toIndex === null || stop.index <= r.toIndex))
              const x = layout.stopX[stop.index]
              return (
                // Test activity (shown only with Hide test activity off) is faded, with a tag.
                <div key={stop.index} style={stop.isTest ? { opacity: 0.45 } : undefined}>
                  <div style={{ ...stopDot, top: lineY - 9, left: x - 9, borderColor: inRun ? COLOURS.run : COLOURS.line, borderStyle: stop.isTest ? 'dashed' : 'solid' }} />
                  <div style={{ ...stopLabel, top: labelTop(layout.stopRow[stop.index]), left: x + 12 }}>
                    <div style={{ fontWeight: 600 }}>
                      {stop.label}{stop.isTest && <span style={testTag}>test</span>}
                    </div>
                    <div style={{ color: '#64748b', fontSize: '0.75rem' }}>
                      {formatTime(stop.at)}{stop.note ? ` · ${stop.note}` : ''}
                    </div>
                  </div>
                </div>
              )
            })}

            {layout.branches.map(({ branch, x, row }) => (
              <div key={`card-${branch.fromIndex}-${branch.kind}`} style={{ ...branchCard, left: x - LAYOUT.branchOffset, top: branchTop(row) }}>
                <div style={{ fontWeight: 600 }}>{branch.label}{branch.count > 1 ? ` ×${branch.count}` : ''}</div>
                <div style={{ color: '#475569', fontSize: '0.75rem' }}>
                  {branch.count > 1 ? 'first ' : ''}{formatTime(branch.firstAt)}
                  {branch.totalSeconds > 0 ? ` · ${formatDuration(branch.totalSeconds)}` : ''}
                </div>
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
const stopDot: React.CSSProperties = {
  position: 'absolute', width: 18, height: 18, boxSizing: 'border-box', borderRadius: '50%',
  background: '#fff', border: '4px solid', zIndex: 1,
}
const stopLabel: React.CSSProperties = { position: 'absolute', width: LAYOUT.labelWidth, fontSize: '0.85rem', lineHeight: 1.3 }
const branchCard: React.CSSProperties = {
  position: 'absolute', width: LAYOUT.branchWidth, height: BRANCH_HEIGHT, boxSizing: 'border-box', padding: '0.4rem 0.6rem',
  background: '#ecfeff', border: `1.5px solid ${COLOURS.branch}`, borderRadius: 8, fontSize: '0.8rem', lineHeight: 1.3,
}
const markBtn = (undo: boolean): React.CSSProperties => ({
  font: 'inherit', fontSize: '0.75rem', padding: '0.2rem 0.55rem', borderRadius: 999, cursor: 'pointer',
  border: `1px solid ${undo ? '#86efac' : '#fcd34d'}`, background: undo ? '#f0fdf4' : '#fffbeb', color: undo ? '#166534' : '#92400e',
})
const testTag: React.CSSProperties = {
  marginLeft: 6, padding: '0 0.35rem', borderRadius: 4, fontSize: '0.65rem', fontWeight: 700,
  background: '#fef3c7', color: '#92400e', verticalAlign: 'middle',
}
const tickLabel: React.CSSProperties = {
  position: 'absolute', top: 0, transform: 'translateX(-50%)', fontSize: '0.7rem', color: '#64748b', whiteSpace: 'nowrap',
}
const segmented: React.CSSProperties = { display: 'inline-flex', border: '1px solid #cbd5e1', borderRadius: 6, overflow: 'hidden' }
const segment = (on: boolean): React.CSSProperties => ({
  font: 'inherit', fontSize: '0.85rem', padding: '0.4rem 0.8rem', border: 'none', cursor: 'pointer',
  background: on ? '#1f6feb' : '#fff', color: on ? '#fff' : '#0f172a', fontWeight: on ? 600 : 400,
})
const registerTable: React.CSSProperties ={ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }
const registerTh: React.CSSProperties = { textAlign: 'left', padding: '0.4rem 0.6rem', borderBottom: '2px solid #e2e8f0', color: '#64748b', fontWeight: 600 }
const registerTd: React.CSSProperties = { padding: '0.35rem 0.6rem', verticalAlign: 'top' }
const statusPill = (status: string): React.CSSProperties => ({
  display: 'inline-block', padding: '0.1rem 0.5rem', borderRadius: 999, fontSize: '0.75rem', fontWeight: 600,
  background: status === 'recording' ? '#dcfce7' : '#fef9c3', color: status === 'recording' ? '#166534' : '#854d0e',
})
