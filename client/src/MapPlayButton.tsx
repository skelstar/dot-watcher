interface Props {
  onPlay: () => void
}

export default function MapPlayButton({ onPlay }: Props) {
  return (
    <button onClick={onPlay} style={button} title="Play from here" aria-label="Play from here">
      <svg width="28" height="34" viewBox="0 0 10 14" fill="white"><polygon points="0,0 10,7 0,14" /></svg>
    </button>
  )
}

const button: React.CSSProperties = {
  position: 'absolute',
  top: '50%',
  left: '50%',
  transform: 'translate(-50%, -50%)',
  width: 72,
  height: 72,
  borderRadius: '50%',
  border: 'none',
  background: 'rgba(0,0,0,0.55)',
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  zIndex: 9,
}
