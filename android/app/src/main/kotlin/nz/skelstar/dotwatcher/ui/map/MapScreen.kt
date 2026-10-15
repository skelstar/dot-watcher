package nz.skelstar.dotwatcher.ui.map

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Path
import android.graphics.PointF
import android.graphics.RectF
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberUpdatedState
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
import org.maplibre.android.style.layers.CircleLayer
import org.maplibre.android.style.layers.PropertyFactory.circleOpacity
import org.maplibre.android.style.layers.PropertyFactory.circleRadius
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeColor
import org.maplibre.android.style.layers.PropertyFactory.circleStrokeWidth
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
import kotlin.math.abs

private const val RUNNERS_SOURCE_ID = "runners-source"
private const val RUNNERS_LAYER_ID = "runners-layer"
private const val FOLLOW_SOURCE_ID = "follow-source"
private const val FOLLOW_LAYER_ID = "follow-layer"
private const val RUNNER_NAME_PROPERTY = "runnerName"
/** Fraction of the visible longitude span the camera may drift from what follow / fit-all aimed
 *  for (after a user gesture) before the app lets go — see the redesign's "Build note". */
private const val RELEASE_THRESHOLD = 0.1
private const val HEADING_PROPERTY = "heading"
private const val ICON_PROPERTY = "icon"

private fun arrowIconId(colorIndex: Int) = "runner-arrow-$colorIndex"
private fun dotIconId(colorIndex: Int) = "runner-dot-$colorIndex"

/**
 * Live map for one session: LINZ topo basemap (matching client/'s style, see
 * client/src/map/mapStyle.ts) plus one marker per runner from [positions], a directional arrow
 * when [RunnerPosition.heading] is present and a plain dot otherwise — mirroring the fallback
 * rule in repo root README.md ("client" section). Each runner is colored via
 * [RunnerColorPalette]; [currentUserName]'s own marker always gets
 * [RunnerColorPalette.CURRENT_USER_INDEX]'s color (blue), the way iOS highlights the local
 * user's own marker.
 *
 * Camera: while [followedRunner] is set the camera tracks that runner; otherwise, while [fitAll],
 * it frames everyone (re-framed on each poll and whenever [fitAllTick] changes). A drag that
 * leaves the camera more than 10% of the visible width from where it was aimed calls
 * [onUserMovedCamera]; zoom alone never does, since it doesn't move the centre. Tapping a runner's
 * marker calls [onRunnerTapped].
 */
@Composable
fun MapScreen(
    positions: List<RunnerPosition>,
    linzApiKey: String,
    currentUserName: String,
    followedRunner: String? = null,
    fitAll: Boolean = true,
    fitAllTick: Int = 0,
    onRunnerTapped: (String) -> Unit = {},
    onUserMovedCamera: () -> Unit = {},
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    val mapView = remember {
        MapLibre.getInstance(context)
        MapView(context)
    }

    var style: Style? by remember { mutableStateOf<Style?>(null) }
    val camera = remember { CameraAim() }
    val currentOnRunnerTapped by rememberUpdatedState(onRunnerTapped)
    val currentOnUserMoved by rememberUpdatedState(onUserMovedCamera)

    // Aim the camera whenever the inputs change. Our own animations are REASON_API_ANIMATION, so
    // they never set [CameraAim.userGesture] and can't trip the release check below.
    LaunchedEffect(style, positions, followedRunner, fitAll, fitAllTick) {
        if (style == null) return@LaunchedEffect
        val followed = followedRunner?.let { name -> positions.firstOrNull { it.runnerName == name } }
        mapView.getMapAsync { map ->
            when {
                followed != null -> {
                    val target = LatLng(followed.latitude, followed.longitude)
                    camera.aimed = target
                    map.animateCamera(CameraUpdateFactory.newLatLng(target))
                }
                fitAll && positions.isNotEmpty() -> {
                    camera.aimed = positions.centre()
                    fitToPositions(map, positions)
                }
                else -> camera.aimed = null
            }
        }
    }

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
                        map.addOnCameraMoveStartedListener { reason ->
                            if (reason == MapLibreMap.OnCameraMoveStartedListener.REASON_API_GESTURE) {
                                camera.userGesture = true
                            }
                        }
                        map.addOnCameraIdleListener {
                            if (!camera.userGesture) return@addOnCameraIdleListener
                            camera.userGesture = false
                            val aimed = camera.aimed ?: return@addOnCameraIdleListener
                            val centre = map.cameraPosition.target ?: return@addOnCameraIdleListener
                            val span = map.projection.visibleRegion.latLngBounds.longitudeSpan
                            val limit = RELEASE_THRESHOLD * span
                            if (abs(centre.longitude - aimed.longitude) > limit ||
                                abs(centre.latitude - aimed.latitude) > limit
                            ) {
                                currentOnUserMoved()
                            }
                        }
                        map.addOnMapClickListener { latLng ->
                            val point: PointF = map.projection.toScreenLocation(latLng)
                            val slop = 28f // finger-sized hit area around the tap
                            val hit = map.queryRenderedFeatures(
                                RectF(point.x - slop, point.y - slop, point.x + slop, point.y + slop),
                                RUNNERS_LAYER_ID,
                            ).firstOrNull()?.getStringProperty(RUNNER_NAME_PROPERTY)
                            if (hit != null) currentOnRunnerTapped(hit)
                            hit != null
                        }
                        val styleUrl = "https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=$linzApiKey"
                        map.setStyle(styleUrl) { loadedStyle ->
                            // One arrow + dot bitmap per palette color/index, registered up front,
                            // rather than per-runner (names vary; the 8-color palette doesn't).
                            RunnerColorPalette.allColors().forEachIndexed { index, color ->
                                loadedStyle.addImage(arrowIconId(index), arrowBitmap(color))
                                loadedStyle.addImage(dotIconId(index), dotBitmap(color))
                            }
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
                            loadedStyle.addSource(GeoJsonSource(FOLLOW_SOURCE_ID))
                            loadedStyle.addLayerBelow(
                                CircleLayer(FOLLOW_LAYER_ID, FOLLOW_SOURCE_ID).withProperties(
                                    circleRadius(22f),
                                    circleOpacity(0f),
                                    circleStrokeColor("#0A84FF"),
                                    circleStrokeWidth(3f),
                                ),
                                RUNNERS_LAYER_ID,
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
                source.setGeoJson(positions.toFeatureCollection(currentUserName))

                val followedPosition = followedRunner?.let { name -> positions.firstOrNull { it.runnerName == name } }
                val followSource = currentStyle.getSourceAs<GeoJsonSource>(FOLLOW_SOURCE_ID)
                if (followedPosition != null) {
                    followSource?.setGeoJson(
                        Feature.fromGeometry(Point.fromLngLat(followedPosition.longitude, followedPosition.latitude))
                    )
                } else {
                    followSource?.setGeoJson(FeatureCollection.fromFeatures(emptyList<Feature>()))
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

/** Mutable camera bookkeeping shared between the effect that aims the camera and the listeners that
 *  decide whether the user has since pulled it away. Not Compose state: it is only read from map
 *  callbacks, never during composition. */
private class CameraAim(var aimed: LatLng? = null, var userGesture: Boolean = false)

private fun List<RunnerPosition>.centre(): LatLng {
    val lat = (minOf { it.latitude } + maxOf { it.latitude }) / 2
    val lng = (minOf { it.longitude } + maxOf { it.longitude }) / 2
    return LatLng(lat, lng)
}

private fun List<RunnerPosition>.toFeatureCollection(currentUserName: String): FeatureCollection {
    val features = map { position ->
        val colorIndex = if (position.runnerName == currentUserName) {
            RunnerColorPalette.CURRENT_USER_INDEX
        } else {
            RunnerColorPalette.indexForName(position.runnerName)
        }
        val point = Point.fromLngLat(position.longitude, position.latitude)
        Feature.fromGeometry(point).apply {
            addStringProperty(RUNNER_NAME_PROPERTY, position.runnerName)
            if (position.heading != null) {
                addNumberProperty(HEADING_PROPERTY, position.heading)
                addStringProperty(ICON_PROPERTY, arrowIconId(colorIndex))
            } else {
                addNumberProperty(HEADING_PROPERTY, 0.0)
                addStringProperty(ICON_PROPERTY, dotIconId(colorIndex))
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

/** Simple upward-pointing triangle in [colorArgb], rotated per-feature by [iconRotate]; stands
 *  in for a designed asset until Milestone 4's own icon pass covers marker artwork too (this
 *  pass covers marker *color*, per [RunnerColorPalette]). */
private fun arrowBitmap(colorArgb: Long): Bitmap {
    val size = 64
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = colorArgb.toInt() }
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

private fun dotBitmap(colorArgb: Long): Bitmap {
    val size = 48
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val paint = Paint(Paint.ANTI_ALIAS_FLAG).apply { color = colorArgb.toInt() }
    canvas.drawCircle(size / 2f, size / 2f, size / 2.5f, paint)
    return bitmap
}
