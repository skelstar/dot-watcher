interface Props {
  x: number
  y: number
  canSendChester: boolean
  canLoadRoute: boolean
  onSendChester: () => void
  onLoadRoute: (file: File) => void
  onClose: () => void
}

export default function MapMenu({ x, y, canSendChester, canLoadRoute, onSendChester, onLoadRoute, onClose }: Props) {
  function handleFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) onLoadRoute(file)
    onClose()
  }

  return (
    <>
      <div onClick={onClose} style={backdrop} />
      <div style={{ ...container, left: x, top: y }}>
        {canSendChester && (
          <div style={item} onClick={() => { onSendChester(); onClose() }}>Send Chester</div>
        )}
        {canLoadRoute && (
          <label style={item}>
            Load route (GPX)
            <input type="file" accept=".gpx" onChange={handleFile} style={{ display: 'none' }} />
          </label>
        )}
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
  display: 'block',
  padding: '15px 24px',
  fontSize: 21,
  fontFamily: 'system-ui, sans-serif',
  cursor: 'pointer',
  color: '#1e293b',
}
