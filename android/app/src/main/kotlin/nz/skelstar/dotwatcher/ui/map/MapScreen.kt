package nz.skelstar.dotwatcher.ui.map

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Color
import android.graphics.Paint
import android.graphics.Path
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalLifecycleOwner
import androidx.compose.ui.unit.dp
import androidx.compose.ui.viewinterop.AndroidView
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleEventObserver
import nz.skelstar.dotwatcher.network.RunnerPosition
import org.maplibre.android.MapLibre
import org.maplibre.android.camera.CameraPosition
import org.maplibre.android.camera.CameraUpdateFactory
import org.maplibre.android.geometry.LatLng
import org.maplibre.android.geometry.LatLngBounds
import org.maplibre.android.maps.MapLibreMap
import org.maplibre.android.maps.MapView
import org.maplibre.android.maps.Style
import org.maplibre.android.style.expressions.Expression.get
import org.maplibre.android.style.layers.PropertyFactory.iconAllowOverlap
import org.maplibre.android.style.layers.PropertyFactory.iconIgnorePlacement
import org.maplibre.android.style.layers.PropertyFactory.iconImage
import org.maplibre.android.style.layers.PropertyFactory.iconRotate
import org.maplibre.android.style.layers.PropertyFactory.iconRotationAlignment
import org.maplibre.android.style.layers.PropertyFactory.iconSize
import org.maplibre.android.style.layers.SymbolLayer
import org.maplibre.android.style.sources.GeoJsonSource
import org.maplibre.geojson.Feature
import org.maplibre.geojson.FeatureCollection
import org.maplibre.geojson.Point

private const val RUNNERS_SOURCE_ID = "runners-source"
private const val RUNNERS_LAYER_ID = "runners-layer"
private const val RUNNER_ARROW_ICON_ID = "runner-arrow"
private const val RUNNER_DOT_ICON_ID = "runner-dot"
private const val HEADING_PROPERTY = "heading"
private const val ICON_PROPERTY = "icon"

/**
 * Live map for one session: LINZ topo basemap (matching client/'s style, see
 * client/src/map/mapStyle.ts) plus one marker per runner from [positions], a directional arrow
 * when [RunnerPosition.heading] is present and a plain dot otherwise — mirroring the fallback
 * rule in repo root README.md ("client" section). Per-runner coloring
 * (client/'s runnerColour, ios/DotWatcher/DotWatcher/RunnerColorPalette.swift) is not yet ported;
 * every marker renders in one accent color for Milestone 1.
 */
@Composable
fun MapScreen(positions: List<RunnerPosition>, linzApiKey: String) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    val mapView = remember {
        MapLibre.getInstance(context)
        MapView(context)
    }

    var style: Style? by remember { mutableStateOf<Style?>(null) }

    DisposableEffect(lifecycleOwner) {
        val observer = LifecycleEventObserver { _, event ->
            when (event) {
                Lifecycle.Event.ON_START -> mapView.onStart()
                Lifecycle.Event.ON_RESUME -> mapView.onResume()
                Lifecycle.Event.ON_PAUSE -> mapView.onPause()
                Lifecycle.Event.ON_STOP -> mapView.onStop()
                Lifecycle.Event.ON_DESTROY -> mapView.onDestroy()
                else -> Unit
            }
        }
        lifecycleOwner.lifecycle.addObserver(observer)
        onDispose {
            lifecycleOwner.lifecycle.removeObserver(observer)
        }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        AndroidView(
            factory = {
                mapView.apply {
                    getMapAsync { map ->
                        val styleUrl = "https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=$linzApiKey"
                        map.setStyle(styleUrl) { loadedStyle ->
                            loadedStyle.addImage(RUNNER_ARROW_ICON_ID, arrowBitmap())
                            loadedStyle.addImage(RUNNER_DOT_ICON_ID, dotBitmap())
                            loadedStyle.addSource(GeoJsonSource(RUNNERS_SOURCE_ID))
                            loadedStyle.addLayer(
                                SymbolLayer(RUNNERS_LAYER_ID, RUNNERS_SOURCE_ID).withProperties(
                                    iconImage(get(ICON_PROPERTY)),
                                    iconRotate(get(HEADING_PROPERTY)),
                                    iconRotationAlignment("map"),
                                    iconAllowOverlap(true),
                                    iconIgnorePlacement(true),
                                    iconSize(1.0f),
                                )
                            )
                            style = loadedStyle
                        }
                    }
                }
            },
            modifier = Modifier.fillMaxSize(),
            update = { view ->
                val currentStyle = style ?: return@AndroidView
                val source = currentStyle.getSourceAs<GeoJsonSource>(RUNNERS_SOURCE_ID) ?: return@AndroidView
                source.setGeoJson(positions.toFeatureCollection())

                if (positions.isNotEmpty()) {
                    view.getMapAsync { map -> fitToPositions(map, positions) }
                }
            },
        )

        if (positions.isEmpty()) {
            Text(
                text = "Waiting for the first position…",
                modifier = Modifier
                    .align(Alignment.TopCenter)
                    .padding(top = 16.dp),
                style = MaterialTheme.typography.bodyMedium,
            )
        }
    }
}

private fun List<RunnerPosition>.toFeatureCollection(): FeatureCollection {
    val features = map { position ->
        val point = Point.fromLngLat(position.longitude, position.latitude)
        Feature.fromGeometry(point).apply {
            addStringProperty("runnerName", position.runnerName)
            if (position.heading != null) {
                addNumberProperty(HEADING_PROPERTY, position.heading)
                addStringProperty(ICON_PROPERTY, RUNNER_ARROW_ICON_ID)
            } else {
                addNumberProperty(HEADING_PROPERTY, 0.0)
                addStringProperty(ICON_PROPERTY, RUNNER_DOT_ICON_ID)
            }
        }
    }
    return FeatureCollection.fromFeatures(features)
}

private fun fitToPositions(map: MapLibreMap, positions: List<RunnerPosition>) {
    if (positions.size == 1) {
        val only = positions.first()
        map.animateCamera(
            CameraUpdateFactory.newCameraPosition(
                CameraPosition.Builder()
                    .target(LatLng(only.latitude, only.longitude))
                    .zoom(15.0)
                    .build()
            )
        )
        return
    }

    val boundsBuilder = LatLngBounds.Builder()
    positions.forEach { boundsBuilder.include(LatLng(it.latitude, it.longitude)) }
    runCatching {
        map.animateCamera(CameraUpdateFactory.newLatLngBounds(boundsBuilder.build(), 96))
    }
}

/** Simple upward-pointing triangle, rotated per-feature by [iconRotate]; stands in for a
 *  designed asset until Milestone 4's icon pass (.ai/plans/android-app.md). */
private fun arrowBitmap(): Bitmap {
    val size = 64
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.parseColor("#FFD60A") }
    val path = Path().apply {
        moveTo(size / 2f, 0f)
        lineTo(size.toFloat(), size.toFloat())
        lineTo(size / 2f, size * 0.72f)
        lineTo(0f, size.toFloat())
        close()
    }
    canvas.drawPath(path, paint)
    return bitmap
}

private fun dotBitmap(): Bitmap {
    val size = 48
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = Color.parseColor("#FFD60A") }
    canvas.drawCircle(size / 2f, size / 2f, size / 2.5f, paint)
    return bitmap
}
