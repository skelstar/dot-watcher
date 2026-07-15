interface Props {
  onLoadRoute: (file: File) => void
  style: React.CSSProperties
}

export default function LoadRouteButton({ onLoadRoute, style }: Props) {
  function handleFile(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0]
    if (file) onLoadRoute(file)
    e.target.value = ''
  }

  return (
    <label style={style} title="Load route (GPX)">
      Load route
      <input type="file" accept=".gpx" onChange={handleFile} style={{ display: 'none' }} />
    </label>
  )
}
