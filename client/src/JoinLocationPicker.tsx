import { useEffect, useRef } from 'react'
import { Map as MapLibreMap, Marker, type GeoJSONSource, type StyleSpecification } from 'maplibre-gl'
import 'maplibre-gl/dist/maplibre-gl.css'

// A small map for the admin Groups tab: click (or drag the pin) to choose a group's join location, with the
// join area drawn around it. OpenStreetMap tiles rather than the LINZ styles the rest of the site uses, since
// LINZ only covers New Zealand and test groups are often wherever the simulators are (Apple Park by default).
// Admin-only and low volume, which OpenStreetMap's tile policy allows with the attribution shown.

const OSM_STYLE: StyleSpecification = {
  version: 8,
  sources: {
    osm: {
      type: 'raster',
      tiles: ['https://tile.openstreetmap.org/{z}/{x}/{y}.png'],
      tileSize: 256,
      maxzoom: 19,
      attribution: '© <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener">OpenStreetMap</a> contributors',
    },
  },
  layers: [{ id: 'osm', type: 'raster', source: 'osm' }],
}

/** Where the iOS Simulator puts a phone unless told otherwise. */
export const SIMULATOR_DEFAULT = { latitude: 37.3349, longitude: -122.009 }

// Where the map opens with no join location yet: Aro Street, Wellington, close in.
const FALLBACK_CENTER: [number, number] = [174.769344, -41.295788]
const FALLBACK_ZOOM = 16

export interface LatLon { latitude: number; longitude: number }

export default function JoinLocationPicker({ value, radiusMeters, onChange }: {
  value: LatLon | null
  radiusMeters: number
  onChange: (point: LatLon) => void
}) {
  const containerRef = useRef<HTMLDivElement>(null)
  const mapRef = useRef<MapLibreMap | null>(null)
  const markerRef = useRef<Marker | null>(null)
  // The latest props for the map's event handlers, which are set up once.
  const onChangeRef = useRef(onChange)
  onChangeRef.current = onChange
  const areaRef = useRef({ value, radiusMeters })
  areaRef.current = { value, radiusMeters }

  useEffect(() => {
    if (!containerRef.current) return
    const map = new MapLibreMap({
      container: containerRef.current,
      style: OSM_STYLE,
      center: value ? [value.longitude, value.latitude] : FALLBACK_CENTER,
      zoom: value ? 16 : FALLBACK_ZOOM,
    })
    map.on('load', () => {
      map.addSource('area', { type: 'geojson', data: emptyArea() })
      map.addLayer({ id: 'area-fill', type: 'fill', source: 'area', paint: { 'fill-color': '#facc15', 'fill-opacity': 0.3 } })
      map.addLayer({ id: 'area-line', type: 'line', source: 'area', paint: { 'line-color': '#a16207', 'line-width': 1.5 } })
      drawArea(map, areaRef.current.value, areaRef.current.radiusMeters)
    })
    map.on('click', e => onChangeRef.current({ latitude: round(e.lngLat.lat), longitude: round(e.lngLat.lng) }))
    map.getCanvas().style.cursor = 'crosshair'
    mapRef.current = map
    return () => {
      map.remove()
      mapRef.current = null
      markerRef.current = null
    }
  }, []) // Made once; the effect below follows the value and radius.

  // The pin and the area follow the chosen point and radius.
  useEffect(() => {
    const map = mapRef.current
    if (!map) return
    if (!value) {
      markerRef.current?.remove()
      markerRef.current = null
    } else {
      if (!markerRef.current) {
        markerRef.current = new Marker({ color: '#1C4733', draggable: true })
          .setLngLat([value.longitude, value.latitude])
          .addTo(map)
        markerRef.current.on('dragend', () => {
          const at = markerRef.current!.getLngLat()
          onChangeRef.current({ latitude: round(at.lat), longitude: round(at.lng) })
        })
      } else {
        markerRef.current.setLngLat([value.longitude, value.latitude])
      }
      // Off screen (a preset, or typed numbers): bring it into view.
      if (!map.getBounds().contains([value.longitude, value.latitude])) {
        map.jumpTo({ center: [value.longitude, value.latitude], zoom: Math.max(map.getZoom(), 15) })
      }
    }
    // Before the map has loaded there's no source yet; the load handler draws it then.
    drawArea(map, value, radiusMeters)
  }, [value, radiusMeters])

  return <div ref={containerRef} style={{ width: '100%', height: 280, borderRadius: 8, overflow: 'hidden' }} />
}

function drawArea(map: MapLibreMap, value: LatLon | null, radiusMeters: number) {
  (map.getSource('area') as GeoJSONSource | undefined)?.setData(value ? circle(value, radiusMeters) : emptyArea())
}

// Six decimal places is about 10 cm, plenty for a join location.
function round(n: number): number {
  return Math.round(n * 1e6) / 1e6
}

function emptyArea(): GeoJSON.FeatureCollection {
  return { type: 'FeatureCollection', features: [] }
}

/** The join area as a 64-sided polygon, close enough to a circle at these sizes. */
function circle(center: LatLon, radiusMeters: number): GeoJSON.Feature<GeoJSON.Polygon> {
  const metresPerDegreeLat = 111_320
  const metresPerDegreeLon = metresPerDegreeLat * Math.cos((center.latitude * Math.PI) / 180)
  const ring: [number, number][] = []
  for (let i = 0; i <= 64; i++) {
    const angle = (i / 64) * 2 * Math.PI
    ring.push([
      center.longitude + (radiusMeters * Math.cos(angle)) / metresPerDegreeLon,
      center.latitude + (radiusMeters * Math.sin(angle)) / metresPerDegreeLat,
    ])
  }
  return { type: 'Feature', properties: {}, geometry: { type: 'Polygon', coordinates: [ring] } }
}
