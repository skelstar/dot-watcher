import L from 'leaflet'

// Small colored-dot markers for phones, and a fixed pin for the convergence point.
// Plain divIcons keep this dependency-free (no extra marker image assets).
export function dotIcon(color: string, size = 18): L.DivIcon {
  return L.divIcon({
    className: '',
    html: `<div style="width:${size}px;height:${size}px;border-radius:50%;background:${color};border:2px solid #fff;box-shadow:0 1px 3px rgba(0,0,0,0.4)"></div>`,
    iconSize: [size, size],
    iconAnchor: [size / 2, size / 2],
  })
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
