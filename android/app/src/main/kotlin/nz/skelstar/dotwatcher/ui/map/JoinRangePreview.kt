package nz.skelstar.dotwatcher.ui.map

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import nz.skelstar.dotwatcher.BuildConfig
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.FillLayer
import org.maplibre.android.style.layers.PropertyFactory.circleColor
import org.maplibre.android.style.layers.PropertyFactory.circleRadius
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth
import org.maplibre.android.style.layers.PropertyFactory.fillColor
import org.maplibre.android.style.layers.PropertyFactory.fillOpacity
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.Feature
import org.maplibre.geojson.Point

private const val PREVIEW_AREA_SOURCE_ID = "preview-area-source"
private const val PREVIEW_AREA_LAYER_ID = "preview-area-layer"
private const val PREVIEW_DOT_SOURCE_ID = "preview-dot-source"
private const val PREVIEW_DOT_LAYER_ID = "preview-dot-layer"
private const val PREVIEW_METRES_PER_DEGREE_LATITUDE = 111_320.0
/** How much wider than the area the view is, so the whole yellow spot shows with a margin. */
private const val PREVIEW_MARGIN = 1.4

/**
 * A small, still map of a join area: the yellow spot of [range] (as on the session map, see [MapScreen]) with a
 * dot at the join location, framed so the whole spot shows. It re-frames when the range changes. It takes no
 * touches, so a drag over it scrolls the dialog or card it sits in. Matches iOS's `NearbyRangePreview`.
 */
@Composable
fun JoinRangePreview(range: JoinRange, modifier: Modifier = Modifier) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current
    val dotColor = MaterialTheme.colorScheme.primary.toArgb()
    val mapView = remember {
        MapLibre.getInstance(context)
        MapView(context)
    }
    var style by remember { mutableStateOf<Style?>(null) }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> mapView.onStart()
                Lifecycle.Event.ON_RESUME -> mapView.onResume()
                Lifecycle.Event.ON_PAUSE -> mapView.onPause()
                Lifecycle.Event.ON_STOP -> mapView.onStop()
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
            // Unlike the full-screen map, this one leaves with its dialog or card while the screen stays.
            mapView.onPause()
            mapView.onStop()
            mapView.onDestroy()
        }
    }

    LaunchedEffect(style, range) {
        val loaded = style ?: return@LaunchedEffect
        loaded.getSourceAs<GeoJsonSource>(PREVIEW_AREA_SOURCE_ID)?.setGeoJson(Feature.fromGeometry(range.toPolygon()))
        loaded.getSourceAs<GeoJsonSource>(PREVIEW_DOT_SOURCE_ID)
            ?.setGeoJson(Feature.fromGeometry(Point.fromLngLat(range.longitude, range.latitude)))
        val halfLat = range.radiusMeters * PREVIEW_MARGIN / PREVIEW_METRES_PER_DEGREE_LATITUDE
        val halfLon = halfLat / kotlin.math.cos(Math.toRadians(range.latitude))
        val bounds = LatLngBounds.Builder()
            .include(LatLng(range.latitude + halfLat, range.longitude + halfLon))
            .include(LatLng(range.latitude - halfLat, range.longitude - halfLon))
            .build()
        mapView.getMapAsync { map -> runCatching { map.animateCamera(CameraUpdateFactory.newLatLngBounds(bounds, 0)) } }
    }

    Box(
        modifier = modifier
            .fillMaxWidth()
            .height(180.dp)
            .clip(RoundedCornerShape(12.dp))
            .semantics { contentDescription = "Map of the join area, ${range.radiusMeters} metres around the join location" },
    ) {
        AndroidView(
            factory = {
                mapView.apply {
                    getMapAsync { map ->
                        map.uiSettings.setAllGesturesEnabled(false)
                        map.uiSettings.isCompassEnabled = false
                        val styleUrl = "https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=${BuildConfig.LINZ_API_KEY}"
                        map.setStyle(styleUrl) { loaded ->
                            loaded.addSource(GeoJsonSource(PREVIEW_AREA_SOURCE_ID))
                            loaded.addLayer(
                                FillLayer(PREVIEW_AREA_LAYER_ID, PREVIEW_AREA_SOURCE_ID).withProperties(
                                    fillColor("#facc15"),
                                    fillOpacity(0.3f),
                                )
                            )
                            loaded.addSource(GeoJsonSource(PREVIEW_DOT_SOURCE_ID))
                            loaded.addLayer(
                                CircleLayer(PREVIEW_DOT_LAYER_ID, PREVIEW_DOT_SOURCE_ID).withProperties(
                                    circleColor(dotColor),
                                    circleRadius(6f),
                                    circleStrokeColor("#ffffff"),
                                    circleStrokeWidth(2f),
                                )
                            )
                            style = loaded
                        }
                    }
                }
            },
            modifier = Modifier.matchParentSize(),
        )
        // Over the map, so touches reach neither the map nor its view; they aren't consumed, so the
        // scrolling dialog or card around it still gets the drag.
        Box(
            modifier = Modifier
                .matchParentSize()
                .pointerInput(Unit) { awaitPointerEventScope { while (true) awaitPointerEvent() } },
        )
    }
}
