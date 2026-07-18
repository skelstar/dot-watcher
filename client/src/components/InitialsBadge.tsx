interface Props {
  name: string
  size?: number
}

// Derives up to two initials from a display name, e.g. "Sean Kelly" -> "SK", "sean" -> "S".
export function initialsFor(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean)
  if (words.length === 0) return ''
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase()
  return (words[0][0] + words[words.length - 1][0]).toUpperCase()
}

// Small circular avatar badge matching the app's logo mark (blue circle, white ring, bold
// white initials), used to put a face to a name inline in copy.
export default function InitialsBadge({ name, size = 20 }: Props) {
  const initials = initialsFor(name)
  if (!initials) return null

  return (
    <span
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        justifyContent: 'center',
        width: size,
        height: size,
        borderRadius: '50%',
        background: '#1f6feb',
        boxShadow: '0 0 0 2px #fff, 0 0 0 3px rgba(0,0,0,0.12)',
        color: '#fff',
        fontFamily: 'system-ui, sans-serif',
        fontWeight: 700,
        fontSize: size * 0.42,
        lineHeight: 1,
        flexShrink: 0,
      }}
    >
      {initials}
    </span>
  )
}
