import L from 'leaflet'
import { ARRIVE_METERS, distanceMeters } from './geo'
import type { LatLon, PhoneStatus, Quality } from './types'

// Mirrors client/src/components/InitialsBadge.tsx exactly, so a phone's tag in this tool
// matches what the real client would show for the same display name.
export function initialsFor(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return ''
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase()
  return (words[0][0] + words[words.length - 1][0]).toUpperCase()
}

// Mirrors client/src/useRunnerMarkers.ts's hash-based palette assignment, so a runner gets
// the same marker color here as on the real client for the same name.
const COLOUR_PALETTE = [
  '#dc2626', // red
  '#0891b2', // teal
  '#d97706', // amber
  '#7c3aed', // violet
  '#16a34a', // green
  '#db2777', // pink
  '#2563eb', // blue
  '#65a30d', // lime
]

function nameHash(name: string): number {
  let h = 0
  for (const c of name) h = (h * 31 + c.charCodeAt(0)) >>> 0
  return h
}

export function runnerColour(name: string): string {
  return COLOUR_PALETTE[nameHash(name) % COLOUR_PALETTE.length]
}

export function convergenceIcon(): L.DivIcon {
  const size = 30
  return L.divIcon({
    className: '',
    html: `<div style="font-size:${size}px;line-height:1;transform:translate(-2px,-4px)">🚩</div>`,
    iconSize: [size, size],
    iconAnchor: [size / 4, size],
  })
}

const SIZE = 48
const CENTER = 24

function chevronMarkup(heading: number): string {
  return `
    <g transform="rotate(${heading}, ${CENTER}, ${CENTER})" style="filter: drop-shadow(0 1px 2px rgba(0,0,0,0.4))">
      <path d="M ${CENTER - 6.5},11 L ${CENTER},4.5 L ${CENTER + 6.5},11"
        fill="none" stroke="#ffffff" stroke-width="4" stroke-linecap="round" stroke-linejoin="round" />
    </g>`
}

function signalLossBadgeMarkup(): string {
  const cx = CENTER + 8.5
  const cy = CENTER + 8.5
  const r = 6
  return `
    <circle cx="${cx}" cy="${cy}" r="${r}" fill="#dc2626" stroke="#ffffff" stroke-width="1.5" />
    <text x="${cx}" y="${cy + r * 0.38}" font-family="Arial, 'Helvetica Neue', sans-serif" font-weight="bold" font-size="${r * 1.3}" fill="#ffffff" text-anchor="middle">!</text>`
}

// Redraws the shape/colour/chevron/badge language of client/src/components/Arrow.tsx as a
// Leaflet divIcon, so phones look the same here as runners do on the real map — except the
// label is always the 2-char initials (the client only does that for its "missing" marker;
// its normal marker spells out the full name, which doesn't fit these smaller tiles/markers).
function runnerMarkerIcon(opts: {
  initials: string
  color: string
  heading: number | null
  missing: boolean
  stationary: boolean
  signalLoss: boolean
}): L.DivIcon {
  const { initials, color, heading, missing, stationary, signalLoss } = opts

  const bodyHtml = missing || stationary
    ? `<div style="position:absolute; left:10px; top:10px; width:28px; height:28px; border-radius:50%;
        background:#ffffff; border:2px ${missing ? 'dashed' : 'solid'} ${color}; box-shadow:0 1px 4px rgba(0,0,0,0.45);
        display:flex; align-items:center; justify-content:center;
        font-family:Arial, 'Helvetica Neue', sans-serif; font-weight:700; font-size:10px; color:${color};">${initials}</div>`
    : `<svg width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}" style="position:absolute; top:0; left:0; filter: drop-shadow(0 1px 4px rgba(0,0,0,0.45));">
        <circle cx="${CENTER}" cy="${CENTER}" r="12" fill="${color}" stroke="#ffffff" stroke-width="3" />
        ${heading !== null ? chevronMarkup(heading) : ''}
        <text x="${CENTER}" y="${CENTER + 4}" font-family="Arial, 'Helvetica Neue', sans-serif" font-weight="bold" font-size="11" fill="#ffffff" text-anchor="middle">${initials}</text>
      </svg>`

  const badgeHtml = signalLoss
    ? `<svg width="${SIZE}" height="${SIZE}" viewBox="0 0 ${SIZE} ${SIZE}" style="position:absolute; top:0; left:0;">${signalLossBadgeMarkup()}</svg>`
    : ''

  return L.divIcon({
    className: '',
    html: `<div style="position:relative; width:${SIZE}px; height:${SIZE}px;">${bodyHtml}${badgeHtml}</div>`,
    iconSize: [SIZE, SIZE],
    iconAnchor: [CENTER, CENTER],
  })
}

// Derives the marker's visual state (missing/stationary/signal-loss) from a phone's current
// quality/status/heading/position the same way the real client derives them from a data gap,
// low displacement, and a null heading, respectively — see useSessionTimelineLogic.ts.
export function phoneMarkerIcon(phone: {
  displayName: string
  quality: Quality
  status: PhoneStatus
  heading: number | null
  position: LatLon | null
  convergencePoint: LatLon | null
  route?: LatLon[] | null
}): L.DivIcon {
  const missing = phone.quality === 'missing' || phone.status === 'left'
  // A phone following a route arrives at the route's last point, not the shared convergence
  // point — the two are mutually exclusive targets (see PhoneSimulator's tick()).
  const target = phone.route && phone.route.length > 0 ? phone.route[phone.route.length - 1] : phone.convergencePoint
  // Gated on 'running' so a phone that merely picked a start point (heading still null because
  // it hasn't sent a reading yet) doesn't pre-emptively look arrived/signal-lost before Start.
  const arrived = !missing && phone.status === 'running' && phone.quality === 'good' && phone.position && target
    ? distanceMeters(phone.position.lat, phone.position.lon, target.lat, target.lon) <= ARRIVE_METERS
    : false
  const signalLoss = !missing && phone.status === 'running' && phone.heading === null

  return runnerMarkerIcon({
    initials: initialsFor(phone.displayName),
    color: runnerColour(phone.displayName),
    heading: phone.heading,
    missing,
    stationary: arrived,
    signalLoss,
  })
}
