import { useState } from 'react'

// Explains the dot states a viewer can see in the Legend (top-left) and on the map markers
// themselves (Arrow.tsx) — a static reference, so it doesn't need any live runner data.
export default function LegendHelp() {
  const [open, setOpen] = useState(false)

  return (
    <>
      <button
        type="button"
        onClick={() => setOpen(true)}
        style={helpButton}
        title="What do the dots mean?"
        aria-label="What do the dots mean?"
      >
        ?
      </button>
      {open && (
        <div style={overlay} onClick={() => setOpen(false)}>
          <div style={dialog} onClick={e => e.stopPropagation()}>
            <div style={header}>
              <h2 style={title}>Dot legend</h2>
              <button type="button" style={closeButton} onClick={() => setOpen(false)}>Close</button>
            </div>

            <div style={entryList}>
              <Entry
                swatch={<SampleDot colour={sampleColour} />}
                label="Reporting normally"
                description="Positions are arriving on schedule."
              />
              <Entry
                swatch={<SampleDot colour={sampleColour} sleeping />}
                label="Sleeping"
                description="The runner hasn't moved in a while, so updates have slowed down to save battery."
              />
              <Entry
                swatch={<SampleDot colour={sampleColour} missing />}
                label="Missing location"
                description="No positions sent at the current time. Maybe finished tracking? Not necessarily a problem."
              />
              <Entry
                swatch={<InlineBadge />}
                label="GPS signal loss"
                description="GPS position may be unreliable right now."
              />
              <Entry
                swatch={<InlineSatellite />}
                label="Satellite connection"
                description="The runner is reporting over a satellite link, so updates arrive less often (to save battery)."
              />
              <Entry
                swatch={<InlineCountdown />}
                label="Update countdown"
                description="Seconds remaining until the next expected position. Only shown when sending less often to save battery power."
              />
            </div>
          </div>
        </div>
      )}
    </>
  )
}

const sampleColour = '#2563eb'

function Entry({ swatch, label, description }: { swatch: React.ReactNode; label: string; description: string }) {
  return (
    <div style={entryRow}>
      <div style={entrySwatch}>{swatch}</div>
      <div>
        <div style={entryLabel}>{label}</div>
        <div style={entryDescription}>{description}</div>
      </div>
    </div>
  )
}

// Miniature stand-ins for the map marker states (Arrow.tsx) / legend pill dot (Legend.tsx),
// simplified to just the shape/border language since colour and initials aren't the point here.
function SampleDot({ colour, missing, sleeping }: { colour: string; missing?: boolean; sleeping?: boolean }) {
  return (
    <div style={{
      width: 28,
      height: 28,
      borderRadius: '50%',
      background: missing || sleeping ? '#ffffff' : colour,
      border: missing ? `2px dashed ${colour}` : `2px solid ${colour}`,
      boxShadow: '0 1px 3px rgba(0,0,0,0.35)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontSize: 9,
      fontFamily: 'system-ui, sans-serif',
      fontWeight: 700,
      color: missing || sleeping ? colour : '#ffffff',
    }}>
      DW
    </div>
  )
}

function InlineBadge() {
  return (
    <div style={{
      width: 18,
      height: 18,
      borderRadius: '50%',
      background: '#dc2626',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
      fontSize: 11,
      fontFamily: 'Arial, sans-serif',
      fontWeight: 700,
      color: '#ffffff',
    }}>
      !
    </div>
  )
}

// Matches the bright yellow circle behind the dish glyph in Legend.tsx's satelliteIconWrap.
function InlineSatellite() {
  return (
    <div style={{
      width: 26,
      height: 26,
      borderRadius: '50%',
      background: '#ffe88e',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
    }}>
      <svg
        viewBox="0 0 24 24"
        width={20}
        height={20}
        fill="none"
        stroke="#000000"
        strokeWidth={2.0}
        strokeLinecap="round"
        strokeLinejoin="round"
      >
        <path d="M4 10a7.31 7.31 0 0 0 10 10Z" />
        <path d="m9 15 3-3" />
        <path d="M17 13a6 6 0 0 0-6-6" />
        <path d="M21 13A10 10 0 0 0 11 3" />
      </svg>
    </div>
  )
}

function InlineCountdown() {
  return (
    <div style={{
      width: 20,
      height: 20,
      borderRadius: '50%',
      border: '1.5px solid #9ca3af',
      background: 'conic-gradient(#6b7280 220deg, transparent 220deg)',
    }} />
  )
}

// Positioned below Mapbox's top-right controls: NavigationControl (zoom +/-, compass, y 10-106)
// stacked above GeolocateControl (y 116-148) — this clears both with a small gap under.
const helpButton: React.CSSProperties = {
  position: 'absolute',
  top: 158,
  right: 10,
  width: 29,
  height: 29,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: '#fff',
  border: 'none',
  borderRadius: 4,
  boxShadow: '0 0 0 2px rgba(0,0,0,0.1)',
  cursor: 'pointer',
  fontSize: '1rem',
  fontFamily: 'system-ui, sans-serif',
  fontWeight: 700,
  color: '#333',
  padding: 0,
  zIndex: 1,
  pointerEvents: 'auto',
}

const overlay: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 12,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  background: 'rgba(0,0,0,0.45)',
}

const dialog: React.CSSProperties = {
  width: 'min(420px, 94vw)',
  maxHeight: '82vh',
  overflowY: 'auto',
  background: '#fff',
  color: '#24292f',
  borderRadius: 8,
  boxShadow: '0 8px 32px rgba(0,0,0,0.25)',
  padding: '1rem',
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
  fontFamily: 'system-ui, sans-serif',
}

const header: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
}

const title: React.CSSProperties = {
  margin: 0,
  fontSize: '1rem',
}

const closeButton: React.CSSProperties = {
  border: '1px solid #d0d7de',
  borderRadius: 4,
  background: '#f6f8fa',
  color: '#24292f',
  cursor: 'pointer',
  fontFamily: 'system-ui, sans-serif',
  fontSize: '0.8rem',
  padding: '5px 8px',
}

const entryList: React.CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: 12,
}

const entryRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  gap: 10,
}

const entrySwatch: React.CSSProperties = {
  flexShrink: 0,
  width: 28,
  height: 28,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
}

const entryLabel: React.CSSProperties = {
  fontSize: '0.85rem',
  fontWeight: 700,
  color: '#1e293b',
}

const entryDescription: React.CSSProperties = {
  fontSize: '0.8rem',
  color: '#57606a',
  marginTop: 2,
}
