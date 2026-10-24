package nz.skelstar.dotwatcher.ui.map

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
import nz.skelstar.dotwatcher.location.nextPostAt
import nz.skelstar.dotwatcher.network.NearbyDot
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
import java.time.Duration
import java.time.Instant
import kotlin.math.abs
import kotlin.math.ceil

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

private const val NEARBY_SOURCE_ID = "nearby-source"
private const val NEARBY_LAYER_ID = "nearby-layer"
private const val NEARBY_IMAGE = "nearby-dot"
private const val NEARBY_LOCKED_IMAGE = "nearby-dot-locked"

private const val CHEVRONS_SOURCE_ID = "chevrons-source"
private const val CHEVRONS_LAYER_ID = "chevrons-layer"

private fun chevronIconId(colorIndex: Int) = "runner-chevron-$colorIndex"

/**
 * Live map for one session: LINZ topo basemap (matching client/'s style, see
 * client/src/map/mapStyle.ts) plus one marker per runner from [positions], with a heading chevron
 * when [RunnerPosition.heading] is present — mirroring the fallback rule in repo root README.md
 * ("client" section). Each runner is colored via
 * [RunnerColorPalette]; [currentUserName]'s own marker always gets
 * [RunnerColorPalette.CURRENT_USER_INDEX]'s color (blue), the way iOS highlights the local
 * user's own marker.
 *
 * Camera: while [followedRunner] is set the camera tracks that runner; otherwise, while [fitAll],
 * it frames everyone (re-framed on each poll and whenever [fitAllTick] changes). A drag that
 * leaves the camera more than 10% of the visible width from where it was aimed calls
 * [onUserMovedCamera]; zoom alone never does, since it doesn't move the centre. Tapping a runner's
 * marker calls [onRunnerTapped].
 *
 * Markers are initials circles (see RunnerPins.kt) with a heading chevron orbiting them. While
 * [sendIntervalSeconds] is set (this device is sharing), the local user's pin wears a ring that
 * empties toward the next clock-aligned position send — unless [ownPaused] (critical battery), when
 * it goes grey with a pause badge and no ring or chevron, since no send is coming.
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
    sendIntervalSeconds: Long? = null,
    ownPaused: Boolean = false,
    /** Casual only: people who are not in the crew, drawn as anonymous grey dots under the runners. */
    nearby: List<NearbyDot> = emptyList(),
    /** Casual only: runners (by name) who are in a named session rather than out casually. */
    lockedRunners: Set<String> = emptySet(),
) {
    val context = LocalContext.current
    val lifecycleOwner = LocalLifecycleOwner.current

    val mapView = remember {
        MapLibre.getInstance(context)
        MapView(context)
    }

    var style: Style? by remember { mutableStateOf<Style?>(null) }
    val camera = remember { CameraAim() }
    val density = context.resources.displayMetrics.density

    // 1 Hz clock: drives stale/disconnected transitions and the send-countdown ring.
    val now by rememberSecondClock()
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
                            // One heading chevron per palette color, registered up front. Pin bitmaps
                            // depend on runner state, so those are registered lazily in `update`.
                            RunnerColorPalette.allColors().forEachIndexed { index, color ->
                                loadedStyle.addImage(chevronIconId(index), chevronBitmap(color, density))
                            }
                            loadedStyle.addSource(GeoJsonSource(CHEVRONS_SOURCE_ID))
                            loadedStyle.addLayer(
                                SymbolLayer(CHEVRONS_LAYER_ID, CHEVRONS_SOURCE_ID).withProperties(
                                    iconImage(get(ICON_PROPERTY)),
                                    iconRotate(get(HEADING_PROPERTY)),
                                    iconRotationAlignment("map"),
                                    iconAllowOverlap(true),
                                    iconIgnorePlacement(true),
                                )
                            )
                            loadedStyle.addImage(NEARBY_IMAGE, nearbyBitmap(locked = false, density = density))
                            loadedStyle.addImage(NEARBY_LOCKED_IMAGE, nearbyBitmap(locked = true, density = density))
                            loadedStyle.addSource(GeoJsonSource(NEARBY_SOURCE_ID))
                            loadedStyle.addLayer(
                                SymbolLayer(NEARBY_LAYER_ID, NEARBY_SOURCE_ID).withProperties(
                                    iconImage(get(ICON_PROPERTY)),
                                    iconAllowOverlap(true),
                                    iconIgnorePlacement(true),
                                )
                            )
                            loadedStyle.addSource(GeoJsonSource(RUNNERS_SOURCE_ID))
                            loadedStyle.addLayer(
                                SymbolLayer(RUNNERS_LAYER_ID, RUNNERS_SOURCE_ID).withProperties(
                                    iconImage(get(ICON_PROPERTY)),
                                    iconAllowOverlap(true),
                                    iconIgnorePlacement(true),
                                )
                            )
                            loadedStyle.addSource(GeoJsonSource(FOLLOW_SOURCE_ID))
                            loadedStyle.addLayerBelow(
                                CircleLayer(FOLLOW_LAYER_ID, FOLLOW_SOURCE_ID).withProperties(
                                    circleRadius(25f),
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
                val ringStep = sendIntervalSeconds?.let { interval ->
                    val remainingMs = Duration.between(now, nextPostAt(now, interval)).toMillis()
                    ceil(RING_STEPS * remainingMs / (interval * 1000.0)).toInt().coerceIn(0, RING_STEPS)
                }
                val specs = positions.associate {
                    it.runnerName to pinSpecFor(it, currentUserName, now, ringStep, ownPaused, it.runnerName in lockedRunners)
                }
                specs.values.distinctBy { it.key }.forEach { spec ->
                    if (currentStyle.getImage(spec.key) == null) {
                        currentStyle.addImage(spec.key, pinBitmap(spec, density))
                    }
                }
                source.setGeoJson(positions.toPinFeatures(specs))
                currentStyle.getSourceAs<GeoJsonSource>(NEARBY_SOURCE_ID)?.setGeoJson(nearby.toNearbyFeatures())
                currentStyle.getSourceAs<GeoJsonSource>(CHEVRONS_SOURCE_ID)
                    ?.setGeoJson(positions.toChevronFeatures(specs))

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

private fun List<NearbyDot>.toNearbyFeatures(): FeatureCollection =
    FeatureCollection.fromFeatures(
        map { dot ->
            Feature.fromGeometry(Point.fromLngLat(dot.longitude, dot.latitude)).apply {
                addStringProperty(ICON_PROPERTY, if (dot.inSession) NEARBY_LOCKED_IMAGE else NEARBY_IMAGE)
            }
        }
    )

private fun List<RunnerPosition>.centre(): LatLng {
    val lat = (minOf { it.latitude } + maxOf { it.latitude }) / 2
    val lng = (minOf { it.longitude } + maxOf { it.longitude }) / 2
    return LatLng(lat, lng)
}

private fun List<RunnerPosition>.toPinFeatures(specs: Map<String, PinSpec>): FeatureCollection =
    FeatureCollection.fromFeatures(
        mapNotNull { position ->
            val spec = specs[position.runnerName] ?: return@mapNotNull null
            Feature.fromGeometry(Point.fromLngLat(position.longitude, position.latitude)).apply {
                addStringProperty(RUNNER_NAME_PROPERTY, position.runnerName)
                addStringProperty(ICON_PROPERTY, spec.key)
            }
        }
    )

/** Chevrons only for runners who are moving and reporting: iOS drops the chevron for stale and
 *  disconnected runners, since a heading from a position that old is misleading. */
private fun List<RunnerPosition>.toChevronFeatures(specs: Map<String, PinSpec>): FeatureCollection =
    FeatureCollection.fromFeatures(
        mapNotNull { position ->
            val spec = specs[position.runnerName] ?: return@mapNotNull null
            val heading = position.heading ?: return@mapNotNull null
            if (spec.state != PinState.Live || spec.paused) return@mapNotNull null
            Feature.fromGeometry(Point.fromLngLat(position.longitude, position.latitude)).apply {
                addNumberProperty(HEADING_PROPERTY, heading)
                addStringProperty(ICON_PROPERTY, chevronIconId(spec.colorIndex))
            }
        }
    )

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
