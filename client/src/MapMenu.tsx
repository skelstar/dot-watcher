interface Props {
  x: number
  y: number
  onSendChester: () => void
  onClose: () => void
}

export default function MapMenu({ x, y, onSendChester, onClose }: Props) {
  return (
    <>
      <div onClick={onClose} style={backdrop} />
      <div style={{ ...container, left: x, top: y }}>
        <div style={item} onClick={() => { onSendChester(); onClose() }}>Send Chester</div>
        <div style={item} onClick={onClose}>Option 2</div>
        <div style={item} onClick={onClose}>Option 3</div>
      </div>
    </>
  )
}

const backdrop: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10,
}

const container: React.CSSProperties = {
  position: 'absolute',
  zIndex: 11,
  background: 'white',
  borderRadius: 12,
  boxShadow: '0 2px 8px rgba(0,0,0,0.25)',
  overflow: 'hidden',
  minWidth: 210,
}

const item: React.CSSProperties = {
  padding: '15px 24px',
  fontSize: 21,
  fontFamily: 'system-ui, sans-serif',
  cursor: 'pointer',
  color: '#1e293b',
}
