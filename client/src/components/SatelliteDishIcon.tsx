// Dish + signal-wave glyph (path data from Lucide's "satellite-dish" icon, ISC licensed) rather
// than the 📡 emoji — emoji rendering is font/OS-dependent and reads as a blurry smudge at small
// sizes; a stroked SVG stays crisp and legible regardless. Shared by the Legend pill and the
// runner dot's satellite badge so both draw the identical glyph.
export default function SatelliteDishIcon({ size }: { size: number }) {
  return (
    <svg
      viewBox="0 0 24 24"
      width={size}
      height={size}
      fill="none"
      stroke="#000000"
      strokeWidth={2.0}
      strokeLinecap="round"
      strokeLinejoin="round"
      style={{ flexShrink: 0 }}
    >
      <path d="M4 10a7.31 7.31 0 0 0 10 10Z" />
      <path d="m9 15 3-3" />
      <path d="M17 13a6 6 0 0 0-6-6" />
      <path d="M21 13A10 10 0 0 0 11 3" />
    </svg>
  )
}
