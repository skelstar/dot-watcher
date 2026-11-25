import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { apiHeaders } from './apiHeaders'
import { initialsFor } from './components/InitialsBadge.tsx'
// The colour each runner has on the map, so a dot looks the same here.
import { runnerColour } from './useRunnerMarkers.ts'
import {
  LAYOUT,
  buildFlow,
  crewGroups,
  dayRange,
  timeForX,
  windowBetween,
  windowScale,
  zoomedScale,
  overlaps,
  runIntervals,
  xForTime,
  formatDuration,
  layoutFlow,
  timeBreaks,
  timeScaleOfWidth,
  timeTicks,
  todayString,
  type ActivityDotDto,
  type ActivityEventDto,
  type Flow,
  type FlowLayout,
  type RegisterEntryDto,
  type TimeScale,
  type TimeWindow,
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
  // Stretches zoomed in to by dragging across a line, each inside the one before. The last is shown;
  // Zoom out goes back one.
  const [zoomedTo, setZoomedTo] = useState<TimeWindow[]>([])
  // Long stretches where no line has anything shrink to a narrow break.
  const [collapseQuiet, setCollapseQuiet] = useState(true)
  // Testing on the live server: activity an admin marked as test is left out unless this is off.
  const [hideTest, setHideTest] = useState(true)
  const [markError, setMarkError] = useState<string | null>(null)
  // How wide the lines can be drawn: the day (or the stretch zoomed in to) fills the panel.
  const [panelWidth, setPanelWidth] = useState(900)
  const panelRef = useRef<HTMLDivElement>(null)
  // Zoomed in, the lines scroll together: each line's scroller. And what's on screen, for the toolbar.
  const scrollers = useRef(new Set<HTMLDivElement>())
  const [onScreen, setOnScreen] = useState<TimeWindow | null>(null)

  const headers = apiHeaders(token, 'web-admin')

  useEffect(() => {
    const panel = panelRef.current
    if (!panel) return
    const observer = new ResizeObserver(entries => setPanelWidth(entries[0].contentRect.width))
    observer.observe(panel)
    return () => observer.disconnect()
  }, [])

  // Esc zooms out one step, as the button does.
  useEffect(() => {
    if (zoomedTo.length === 0) return
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setZoomedTo(current => current.slice(0, -1))
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [zoomedTo.length])

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
  const dayEvents = events.kind === 'ok' ? events.value.filter(e => selected.includes(e.userId)) : []
  const zoomWindow = zoomedTo[zoomedTo.length - 1] ?? null
  const shownEvents = dayEvents
  // Every line shares one scale, so the same moment is at the same place on each. Zoomed out it fits the
  // panel; zoomed in, the stretch fills the panel and the rest of the day scrolls either side.
  // A line in a crew box has that box's edges to fit inside as well.
  const inCrewBox = crewGroups(dotList.filter(dot => selected.includes(dot.userId))).some(group => group.length > 1)
  const fitWidth = Math.max(300,
    panelWidth - LINE_CARD_INSET - (inCrewBox ? CREW_BOX_INSET : 0) - LAYOUT.startX - LAYOUT.labelWidth - 28)
  // Zoomed in: the day drawn straight at the stretch's rate. Otherwise the day's events, with quiet
  // stretches shrunk to breaks if asked; with nothing to show, the day itself, so there's always a scale.
  const { from: dayFrom, to: dayTo } = dayRange(day)
  const scale: TimeScale = zoomWindow
    ? zoomedScale(shownEvents, zoomWindow, fitWidth)
    : timeScaleOfWidth(shownEvents, fitWidth, collapseQuiet)
      ?? windowScale({ start: Date.parse(dayFrom), end: Date.parse(dayTo) }, fitWidth)

  // What's on screen, from where a line is scrolled to.
  const onScreenAt = (scrollLeft: number): TimeWindow => ({
    start: timeForX(scale, scrollLeft + LAYOUT.startX),
    end: timeForX(scale, scrollLeft + LAYOUT.startX + fitWidth),
  })

  // On zooming in (or back out a step), every line scrolls so the stretch fills the panel.
  const zoomLeft = zoomWindow ? Math.max(0, xForTime(scale, zoomWindow.start) - LAYOUT.startX) : 0
  useLayoutEffect(() => {
    for (const el of scrollers.current) el.scrollLeft = zoomLeft
    setOnScreen(zoomWindow ? onScreenAt(zoomLeft) : null)
    // Only on a new stretch or a new scale width (a resize), not every render, so it doesn't undo scrolling.
  }, [zoomWindow, zoomLeft, scale.width])

  // The others follow the one scrolled. Their own scroll events then find everyone already in step, so
  // nothing bounces back.
  function syncScroll(source: HTMLDivElement) {
    if (!zoomWindow) return
    for (const other of scrollers.current)
      if (other !== source && other.scrollLeft !== source.scrollLeft) other.scrollLeft = source.scrollLeft
    setOnScreen(onScreenAt(source.scrollLeft))
  }

  function registerScroller(el: HTMLDivElement | null) {
    if (!el) return
    scrollers.current.add(el)
    // A line that appears while zoomed in starts where the others are.
    const other = [...scrollers.current].find(s => s !== el)
    if (other) el.scrollLeft = other.scrollLeft
    return () => { scrollers.current.delete(el) }
  }
  const laidOut = dotList
    .filter(dot => selected.includes(dot.userId))
    .map(dot => {
      const flow = buildFlow(shownEvents.filter(e => e.userId === dot.userId), register)
      return { dot, flow, layout: layoutFlow(flow, scale) }
    })
  // Every line is as wide as the widest, so they line up.
  const sharedWidth = Math.max(0, ...laidOut.map(l => l.layout.width))

  // Crew: lines of dots who are crew sit together in one box, and each line shows when its crew were out
  // with it (both with a Go run going).
  const lineById = new Map(laidOut.map(line => [line.dot.userId, line]))
  const endOfLine = scale.end
  const togetherFor = (dot: ActivityDotDto, flow: Flow) => (dot.crewWith ?? [])
    .map(id => lineById.get(id))
    .filter((mate): mate is NonNullable<typeof mate> => !!mate)
    .map(mate => ({
      mate: mate.dot,
      spans: overlaps(runIntervals(flow, endOfLine), runIntervals(mate.flow, endOfLine)),
    }))
    .filter(t => t.spans.length > 0)
  const groups = crewGroups(laidOut.map(line => line.dot))

  const renderLine = ({ dot, flow, layout }: (typeof laidOut)[number]) => (
    <DotLine
      key={dot.userId}
      dot={dot}
      flow={flow}
      layout={layout}
      scale={scale}
      width={sharedWidth}
      together={togetherFor(dot, flow)}
      scrollable={zoomWindow !== null}
      scrollerRef={registerScroller}
      onScroll={syncScroll}
      onMark={(runId, isTest) => markTest(dot.userId, runId, isTest)}
      onZoomTo={window => setZoomedTo(current => [...current, window])}
      onZoomOut={zoomWindow ? () => setZoomedTo(current => current.slice(0, -1)) : undefined}
    />
  )

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
        What each dot did, every line on one clock, so you can see what happened when, relative to the others.
        Drag across a line to zoom in to that stretch, then scroll along the day; click a line, Zoom out or Esc
        to go back.
        Orange: a Go run. Dashed: the app was in the background. 🔗 on the line: a watch link was shared.
        Someone else shows below the line: a 👁️ strip for each viewing, as long as they had it on screen.
        Crew are boxed together, and each line shows when its crew were out at the same time (a strip in
        their colour, starting with their dot).
        Events are kept for 30 days.
      </p>

      {/* One toolbar row, every control the same height: the day, two toggles, the zoom (when zoomed in),
          and Refresh at the far end. */}
      <div style={controls}>
        <label style={dayField}>
          <span style={dayFieldLabel}>Day</span>
          <input type="date" value={day} max={todayString()} onChange={e => {
            if (!e.target.value) return
            setDay(e.target.value)
            // A new day starts zoomed out.
            setZoomedTo([])
          }} style={dateInput} />
        </label>
        <label style={toggleChip(collapseQuiet, zoomWindow !== null)}
          title={zoomWindow
            ? 'Zoomed in, the stretch is drawn straight: zoom out to use this'
            : 'Long stretches where no line has anything are shrunk to a break marked with how long they were'}>
          <input type="checkbox" checked={collapseQuiet} onChange={e => setCollapseQuiet(e.target.checked)} style={toggleBox}
            disabled={zoomWindow !== null} />
          Collapse quiet stretches
        </label>
        <label style={toggleChip(hideTest, false)}
          title="Runs and stretches marked as test (testing on the live server) are left out">
          <input type="checkbox" checked={hideTest} onChange={e => setHideTest(e.target.checked)} style={toggleBox} />
          Hide test activity
        </label>
        {zoomWindow && (
          <div style={zoomGroup} role="group" aria-label="Zoom">
            {/* What's on screen: the stretch dragged across, then wherever the lines are scrolled to. */}
            <span style={zoomChip}>{formatTime((onScreen ?? zoomWindow).start)}–{formatTime((onScreen ?? zoomWindow).end)}</span>
            <button style={toolbarBtn} onClick={() => setZoomedTo(current => current.slice(0, -1))}
              title="Back to how it was before the last drag (Esc, or click a line)">
              Zoom out
            </button>
            {zoomedTo.length > 1 && (
              <button style={toolbarBtn} onClick={() => setZoomedTo([])}>Whole day</button>
            )}
          </div>
        )}
        <button style={{ ...toolbarBtn, marginLeft: 'auto' }} onClick={() => setRefreshTick(t => t + 1)}>Refresh</button>
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
              <span style={{ ...chipBadge, background: runnerColour(dot.displayName) }}>{initialsFor(dot.displayName)}</span>
              {dot.displayName} <span style={{ color: '#64748b' }}>({dot.eventCount})</span>
            </label>
          ))}
        </div>
      )}

      {events.kind === 'error' && <p style={errorText}>{events.message}</p>}
      <div ref={panelRef}>
        {groups.map(group => group.length === 1
          ? renderLine(lineById.get(group[0].userId)!)
          : (
            <section key={group.map(d => d.userId).join('|')} style={crewBox} aria-label="Crew">
              <div style={crewBoxHeader}>
                Crew
                {group.map(d => <span key={d.userId} style={{ ...chipBadge, background: runnerColour(d.displayName) }}>{initialsFor(d.displayName)}</span>)}
              </div>
              {group.map(d => renderLine(lineById.get(d.userId)!))}
            </section>
          ))}
      </div>

      <RegisterList register={register} />
    </>
  )
}

// ---- One dot's line ----------------------------------------------------------------------------

// A drag narrower than this is taken as a click, not a zoom.
const MIN_DRAG_PX = 8
// A line card's horizontal padding and border, both sides.
const LINE_CARD_INSET = 34
// A crew box's horizontal padding and borders (4 px left, 1 px right, 10 px padding each side).
const CREW_BOX_INSET = 25
const LABEL_ROW = 42
// One strip per crew mate (and one for watching), with their dot (or 👁️) at its start.
const CREW_ROW = 23
// The marker at a strip's start: a crew mate's dot, or the watching icon at a matching size (an emoji
// has space around it, so it's set a touch bigger to look the same).
const STRIP_MARKER = CREW_ROW - 3
// How far down a label its first line's middle is, where the leader's tick meets it.
const LEADER_TICK_Y = 9
const BRANCH_HEIGHT = 52
// A stop with an icon is drawn on the line as a dot this big, with the icon in it.
const ICON_DOT = 24
const BRANCH_GAP = 12
const COLOURS = { line: '#334155', run: '#ea580c', branch: '#0e7490', grid: '#e2e8f0' }

function formatTime(at: string | number, withSeconds = true) {
  return new Date(at).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit', ...(withSeconds ? { second: '2-digit' } : {}) })
}

type Together = { mate: ActivityDotDto; spans: { start: number; end: number }[] }

function DotLine({ dot, flow, layout, scale, width, together, scrollable, scrollerRef, onScroll, onMark, onZoomTo, onZoomOut }: {
  dot: ActivityDotDto
  flow: Flow
  layout: FlowLayout
  scale: TimeScale
  width: number
  // Crew who were out at the same time as this dot, and when.
  together: Together[]
  // Zoomed in: the line is wider than the panel and scrolls (with the others).
  scrollable: boolean
  scrollerRef: (el: HTMLDivElement | null) => (() => void) | undefined
  onScroll: (el: HTMLDivElement) => void
  onMark: (runId: string, isTest: boolean) => void
  // A stretch dragged across, to zoom in to.
  onZoomTo: (window: TimeWindow) => void
  // Zoomed in: a plain click on the line goes back a step. Absent when zoomed out.
  onZoomOut?: () => void
}) {
  // A drag across the line in progress: where it started and where the pointer is, in px.
  const [drag, setDrag] = useState<{ from: number; to: number } | null>(null)
  const dragLeft = drag ? Math.min(drag.from, drag.to) : 0
  const dragRight = drag ? Math.max(drag.from, drag.to) : 0
  const xIn = (e: React.PointerEvent<HTMLDivElement>) =>
    Math.min(Math.max(e.clientX - e.currentTarget.getBoundingClientRect().left, 0), width)
  const zoomDrag = {
    onPointerDown(e: React.PointerEvent<HTMLDivElement>) {
      // Left button only, and not on something that does its own thing when clicked.
      if (e.button !== 0 || (e.target as HTMLElement).closest('button, a, input, select')) return
      e.preventDefault()
      e.currentTarget.setPointerCapture(e.pointerId)
      const x = xIn(e)
      setDrag({ from: x, to: x })
    },
    onPointerMove(e: React.PointerEvent<HTMLDivElement>) {
      if (drag) setDrag({ ...drag, to: xIn(e) })
    },
    onPointerUp() {
      if (!drag) return
      if (dragRight - dragLeft >= MIN_DRAG_PX)
        onZoomTo(windowBetween(timeForX(scale, dragLeft), timeForX(scale, dragRight)))
      // A click (or a drag too small to mean anything) zooms out a step, when zoomed in.
      else onZoomOut?.()
      setDrag(null)
    },
    onPointerCancel() { setDrag(null) },
  }
  // Strips just under the line: first one for watching (each viewing along it, 👁️ at its start),
  // then one per crew mate in their colour, for when both were out.
  const watchRows = layout.branches.some(b => b.placement === 'strip') ? 1 : 0
  const crewRows = together.length
  const stripY = (row: number) => lineY + 14 + row * CREW_ROW
  const crewY = (row: number) => stripY(watchRows + row)
  // The Go runs on this line, for their mark buttons. A run is test when its start (or stop) is.
  const runs = flow.runs
    .filter(run => run.runId)
    .map(run => {
      const first = flow.stops[run.fromIndex]
      const last = run.toIndex === null ? null : flow.stops[run.toIndex]
      return { runId: run.runId!, first, last, isTest: first.isTest || (last?.isTest ?? false) }
    })
  // Gridline labels sit above the line.
  const lineY = 52
  // A branch without an icon still gets a thin bar per span just under the line.
  const spanY = lineY + 13
  // Labels start below the strips.
  const labelTop = (row: number) => lineY + 22 + (watchRows + crewRows) * CREW_ROW + row * LABEL_ROW
  const labelsBottom = labelTop(layout.labelRows)
  // Cards go in rows below the labels, so the line's box is only as deep as its labels unless there are cards.
  const branchTop = (row: number) => labelsBottom + 16 + row * (BRANCH_HEIGHT + BRANCH_GAP)
  const height = layout.branchRows > 0 ? branchTop(layout.branchRows - 1) + BRANCH_HEIGHT + 8 : labelsBottom + 4
  const ticks = timeTicks(scale)
  const breaks = timeBreaks(scale)
  const rail: React.CSSProperties = { position: 'absolute', top: lineY - 2, height: 4, borderRadius: 4 }

  return (
    <section style={lineCard} aria-label={`${dot.displayName}'s activity`}>
      <div style={lineHeader}>
        <span style={{ ...lineBadge, background: runnerColour(dot.displayName) }}>{initialsFor(dot.displayName)}</span>
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
        // Zoomed out the line fits the panel, and anything a pixel or two over (rounding) is trimmed rather
        // than scrolled. Zoomed in it scrolls.
        <div style={{ overflowX: scrollable ? 'auto' : 'hidden' }} ref={scrollerRef} onScroll={e => onScroll(e.currentTarget)}>
          <div style={{ position: 'relative', width, height, cursor: 'crosshair', userSelect: 'none' }} {...zoomDrag}>
            {/* Gridlines first, so everything else draws over them. */}
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

            {/* Collapsed: a quiet stretch (nothing on any line) shrunk to a break between two dashed
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

            {/* When each crew mate was out too, a strip per mate in their colour. */}
            {together.flatMap((t, row) => t.spans.map((span, i) => {
              const left = xForTime(scale, span.start)
              const colour = runnerColour(t.mate.displayName)
              return (
                <div key={`crew-${t.mate.userId}-${i}`}
                  title={`${t.mate.displayName} was out too, ${formatTime(span.start, false)}–${formatTime(span.end, false)}`}
                  style={{
                    position: 'absolute', top: crewY(row), height: CREW_ROW - 3, left, width: Math.max(xForTime(scale, span.end) - left, CREW_ROW),
                    borderRadius: 999, background: `${colour}33`, boxSizing: 'border-box', display: 'flex', alignItems: 'center',
                  }}>
                  {/* Their dot, as in the picker: the strip says who without words. */}
                  <span aria-label={`with ${t.mate.displayName}`} style={{ ...crewMateDot, background: colour }}>
                    {initialsFor(t.mate.displayName)}
                  </span>
                </div>
              )
            }))}

            {/* The watching strip: each viewing from when it started to when it ended, its icon
                at the start, the length (or that it's still going) on hover. */}
            {layout.branches.filter(b => b.placement === 'strip').flatMap(({ branch, spans }) => spans.map((span, i) => {
              const seconds = branch.spans[i].end === null ? null : Math.round((branch.spans[i].end! - branch.spans[i].start) / 1000)
              return (
                <div key={`watch-${branch.fromIndex}-${branch.kind}-${i}`}
                  title={`${branch.label} · ${formatTime(branch.spans[i].start)} · ${seconds === null ? 'still watching, or length not known yet' : formatDuration(seconds)}`}
                  style={{
                    position: 'absolute', top: stripY(0), height: CREW_ROW - 3, left: span.left,
                    width: Math.max(span.right - span.left, CREW_ROW), borderRadius: 999, boxSizing: 'border-box',
                    display: 'flex', alignItems: 'center',
                    background: span.ongoing ? `linear-gradient(to right, ${COLOURS.branch}33, transparent)` : `${COLOURS.branch}33`,
                  }}>
                  <span role="img" aria-label={branch.label} style={stripIcon}>{branch.icon}</span>
                </div>
              )
            }))}

            {/* A card's spans (a branch without an icon): a thin bar under the line. */}
            {layout.branches.filter(b => b.placement === 'cards').flatMap(({ branch, spans }) => spans.map((span, i) => (
              <div key={`span-${branch.fromIndex}-${branch.kind}-${i}`}
                style={{
                  position: 'absolute', top: spanY, height: 4, borderRadius: 2, left: span.left, width: span.right - span.left,
                  background: span.ongoing ? `linear-gradient(to right, ${COLOURS.branch}, transparent)` : COLOURS.branch,
                }} />
            )))}

            {layout.branches.filter(b => b.placement === 'cards').map(placed => (
              <div key={`stem-${placed.branch.fromIndex}-${placed.branch.kind}`}
                style={{ position: 'absolute', left: placed.x - 1, top: lineY, width: 2, height: branchTop(placed.row) - lineY, background: COLOURS.branch }} />
            ))}

            {flow.stops.map(stop => {
              const inRun = flow.runs.some(r => stop.index >= r.fromIndex && (r.toIndex === null || stop.index <= r.toIndex))
              const x = layout.stopX[stop.index]
              return (
                // Test activity (shown only with Hide test activity off) is faded, with a tag.
                <div key={stop.index} style={stop.isTest ? { opacity: 0.45 } : undefined}>
                  {stop.icon ? (
                    // An event with an icon sits on the line: its dot holds the icon, and there's no label
                    // (the label and time on hover).
                    <div title={`${stop.label} · ${formatTime(stop.at)}${stop.note ? ` · ${stop.note}` : ''}${stop.isTest ? ' · test' : ''}`}
                      style={{
                        ...stopDot, ...iconDot, top: lineY - ICON_DOT / 2, left: x - ICON_DOT / 2,
                        borderColor: inRun ? COLOURS.run : COLOURS.line, borderStyle: stop.isTest ? 'dashed' : 'solid',
                      }}>
                      <span role="img" aria-label={stop.label}>{stop.icon}</span>
                    </div>
                  ) : (
                    <>
                      {/* A thin leader from the dot down to its label, and a tick into the label's first line,
                          so a label on a lower row is easy to match to its dot. */}
                      <div style={{ ...leader, left: x - 0.5, top: lineY + 9, height: labelTop(layout.stopRow[stop.index]) + LEADER_TICK_Y - lineY - 9 }} />
                      <div style={{ ...leader, left: x, top: labelTop(layout.stopRow[stop.index]) + LEADER_TICK_Y, width: 9, height: 1 }} />
                      <div style={{ ...stopDot, top: lineY - 9, left: x - 9, borderColor: inRun ? COLOURS.run : COLOURS.line, borderStyle: stop.isTest ? 'dashed' : 'solid' }} />
                    </>
                  )}
                  {stop.icon ? null : (
                    <div style={{ ...stopLabel, top: labelTop(layout.stopRow[stop.index]), left: x + 12 }}>
                      <div style={{ fontWeight: 600 }}>
                        {stop.label}{stop.isTest && <span style={testTag}>test</span>}
                      </div>
                      <div style={{ color: '#64748b', fontSize: '0.75rem' }}>
                        {formatTime(stop.at)}{stop.note ? ` · ${stop.note}` : ''}
                      </div>
                    </div>
                  )}
                </div>
              )
            })}

            {layout.branches.filter(b => b.placement === 'cards').map(placed => {
              const { branch, x } = placed
              const top = branchTop(placed.row)
              const length = branch.totalSeconds > 0 ? formatDuration(branch.totalSeconds) : null
              return (
                <div key={`card-${branch.fromIndex}-${branch.kind}`} style={{ ...branchCard, left: x - LAYOUT.branchOffset, top }}>
                  <div style={{ fontWeight: 600 }}>{branch.label}{branch.count > 1 ? ` ×${branch.count}` : ''}</div>
                  <div style={{ color: '#475569', fontSize: '0.75rem' }}>
                    {branch.count > 1 ? 'first ' : ''}{formatTime(branch.firstAt)}{length ? ` · ${length}` : ''}
                  </div>
                </div>
              )
            })}

            {/* A drag in progress: the stretch it would zoom in to, with its times. */}
            {drag && dragRight - dragLeft >= MIN_DRAG_PX && (
              <div style={{ ...dragBox, left: dragLeft, width: dragRight - dragLeft }}>
                <span style={dragTimes}>{formatTime(timeForX(scale, dragLeft))}–{formatTime(timeForX(scale, dragRight))}</span>
              </div>
            )}
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
                <td style={registerTd}>{entry.icon ? `${entry.icon} ` : ''}{entry.label}</td>
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
// The toolbar: one row, every control CONTROL_HEIGHT tall with the same border and radius, so they line up.
const CONTROL_HEIGHT = 34
const control: React.CSSProperties = {
  boxSizing: 'border-box', height: CONTROL_HEIGHT, display: 'inline-flex', alignItems: 'center',
  border: '1px solid #cbd5e1', borderRadius: 8, background: '#fff', font: 'inherit', fontSize: '0.85rem', color: '#0f172a',
}
const controls: React.CSSProperties = {
  display: 'flex', flexWrap: 'wrap', gap: '0.5rem', alignItems: 'center', marginBottom: '0.5rem',
  padding: '0.5rem', background: '#f8fafc', border: '1px solid #e2e8f0', borderRadius: 10,
}
// The day: a label and the date input joined into one control.
const dayField: React.CSSProperties = { ...control, overflow: 'hidden' }
const dayFieldLabel: React.CSSProperties = {
  alignSelf: 'stretch', display: 'flex', alignItems: 'center', padding: '0 0.65rem', background: '#f1f5f9',
  borderRight: '1px solid #cbd5e1', color: '#64748b', fontSize: '0.8rem', fontWeight: 600,
}
const dateInput: React.CSSProperties = {
  font: 'inherit', fontSize: '0.85rem', height: '100%', padding: '0 0.5rem', border: 'none', outline: 'none', background: 'transparent', color: '#0f172a',
  fontVariantNumeric: 'tabular-nums',
}
// A checkbox as a toggle chip: tinted when on, faded when it can't be changed.
const toggleChip = (on: boolean, disabled: boolean): React.CSSProperties => ({
  ...control, gap: 6, padding: '0 0.75rem', cursor: disabled ? 'not-allowed' : 'pointer', userSelect: 'none',
  ...(on ? { background: '#eff6ff', borderColor: '#93c5fd', color: '#1e3a8a' } : {}),
  ...(disabled ? { opacity: 0.5 } : {}),
})
const toggleBox: React.CSSProperties = { margin: 0, accentColor: '#2563eb', cursor: 'inherit' }
const toolbarBtn: React.CSSProperties = { ...control, padding: '0 0.85rem', cursor: 'pointer', fontWeight: 500 }
const zoomGroup: React.CSSProperties = { display: 'inline-flex', alignItems: 'center', gap: '0.5rem' }
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
const lineHeader: React.CSSProperties = { display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 8, marginBottom: '0.25rem' }
const lineBadge: React.CSSProperties = { ...chipBadge, width: 28, height: 28, fontSize: '0.75rem' }
const stopDot: React.CSSProperties = {
  position: 'absolute', width: 18, height: 18, boxSizing: 'border-box', borderRadius: '50%',
  background: '#fff', border: '4px solid', zIndex: 1,
}
const stopLabel: React.CSSProperties = {
  position: 'absolute', width: LAYOUT.labelWidth, fontSize: '0.85rem', lineHeight: 1.3,
  // Over other stops' leaders, so a leader passing a label on a row above doesn't strike through it.
  background: 'rgba(255,255,255,0.88)', zIndex: 1,
}
const branchCard: React.CSSProperties = {
  position: 'absolute', width: LAYOUT.branchWidth, height: BRANCH_HEIGHT, boxSizing: 'border-box', padding: '0.4rem 0.6rem',
  background: '#ecfeff', border: `1.5px solid ${COLOURS.branch}`, borderRadius: 8, fontSize: '0.8rem', lineHeight: 1.3,
}
// A stop with an icon: a bigger white dot on the line with the icon in it.
const iconDot: React.CSSProperties = {
  width: ICON_DOT, height: ICON_DOT, borderWidth: 3, display: 'flex', alignItems: 'center', justifyContent: 'center',
  fontSize: '0.72rem', lineHeight: 1, cursor: 'default', zIndex: 2,
}
// The icon at the start of a watching strip: just the icon, as big as a crew mate's dot.
const stripIcon: React.CSSProperties = {
  width: STRIP_MARKER, height: STRIP_MARKER, flexShrink: 0,
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
  fontSize: `${STRIP_MARKER - 1}px`, lineHeight: 1,
}
const leader: React.CSSProperties = { position: 'absolute', width: 1, background: '#475569', opacity: 0.6, pointerEvents: 'none' }
const crewMateDot: React.CSSProperties = {
  width: STRIP_MARKER, height: STRIP_MARKER, borderRadius: '50%', flexShrink: 0, color: '#fff',
  display: 'inline-flex', alignItems: 'center', justifyContent: 'center', fontSize: '0.6rem', fontWeight: 700,
}
const crewBox: React.CSSProperties = {
  border: '1px solid #ddd6fe', borderLeft: '4px solid #7c3aed', borderRadius: 12, padding: '0.5rem 10px 0.1rem',
  marginBottom: '0.75rem', background: '#faf5ff',
}
const crewBoxHeader: React.CSSProperties = {
  display: 'flex', alignItems: 'center', gap: 6, fontSize: '0.75rem', fontWeight: 700, color: '#6d28d9',
  textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '0.4rem',
}
const markBtn = (undo: boolean): React.CSSProperties => ({
  font: 'inherit', fontSize: '0.75rem', padding: '0.2rem 0.55rem', borderRadius: 999, cursor: 'pointer',
  border: `1px solid ${undo ? '#86efac' : '#fcd34d'}`, background: undo ? '#f0fdf4' : '#fffbeb', color: undo ? '#166534' : '#92400e',
})
const testTag: React.CSSProperties = {
  marginLeft: 6, padding: '0 0.35rem', borderRadius: 4, fontSize: '0.65rem', fontWeight: 700,
  background: '#fef3c7', color: '#92400e', verticalAlign: 'middle',
}
const dragBox: React.CSSProperties = {
  position: 'absolute', top: 0, bottom: 0, zIndex: 3, pointerEvents: 'none', boxSizing: 'border-box',
  background: 'rgba(37, 99, 235, 0.12)', borderLeft: '1px solid #2563eb', borderRight: '1px solid #2563eb',
}
const dragTimes: React.CSSProperties = {
  position: 'absolute', top: 2, left: '50%', transform: 'translateX(-50%)', padding: '1px 6px', borderRadius: 4,
  background: '#2563eb', color: '#fff', fontSize: '0.7rem', fontWeight: 600, whiteSpace: 'nowrap',
}
// The stretch zoomed in to, as a control-height chip beside its buttons.
const zoomChip: React.CSSProperties = {
  ...control, padding: '0 0.75rem', fontWeight: 600, background: '#dbeafe', borderColor: '#93c5fd', color: '#1e40af',
  fontVariantNumeric: 'tabular-nums',
}
const tickLabel: React.CSSProperties = {
  position: 'absolute', top: 0, transform: 'translateX(-50%)', fontSize: '0.7rem', color: '#64748b', whiteSpace: 'nowrap',
}
const registerTable: React.CSSProperties ={ width: '100%', borderCollapse: 'collapse', fontSize: '0.85rem' }
const registerTh: React.CSSProperties = { textAlign: 'left', padding: '0.4rem 0.6rem', borderBottom: '2px solid #e2e8f0', color: '#64748b', fontWeight: 600 }
const registerTd: React.CSSProperties = { padding: '0.35rem 0.6rem', verticalAlign: 'top' }
const statusPill = (status: string): React.CSSProperties => ({
  display: 'inline-block', padding: '0.1rem 0.5rem', borderRadius: 999, fontSize: '0.75rem', fontWeight: 600,
  background: status === 'recording' ? '#dcfce7' : '#fef9c3', color: status === 'recording' ? '#166534' : '#854d0e',
})
