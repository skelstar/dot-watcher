package nz.skelstar.dotwatcher.ui.groups

import androidx.compose.foundation.Canvas
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Button
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.shadow
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Dialog
import androidx.compose.ui.window.DialogProperties
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.BuildConfig
import nz.skelstar.dotwatcher.groups.GroupOrganiserActions
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.route.RoutePoint
import nz.skelstar.dotwatcher.ui.map.JoinRange
import nz.skelstar.dotwatcher.ui.map.MapScreen

// Setting a group's join location at a point on the map instead of where the organiser is standing: a crosshair
// in the middle of the map marks the point. Two ways in, as on iOS (JoinLocationOnMap.swift): "Choose on map" on
// the organiser's group card ([JoinLocationPickerDialog], works without going out), and on the Go map once it's
// dragged off the person's dot ([MapCrosshairActionBar]).

private const val METRES_PER_DEGREE_LATITUDE = 111_320.0

/** The small cross in the middle of the map: black with a white edge, so it shows on any basemap colour. Draws
 *  only, so the map still drags under it. */
@Composable
fun MapCrosshair(modifier: Modifier = Modifier, length: Dp = 22.dp) {
    Canvas(modifier = modifier.size(length + 4.dp).clearAndSetSemantics {}) {
        val mid = Offset(size.width / 2, size.height / 2)
        fun cross(halfLength: Float, width: Float, color: Color) {
            drawLine(color, Offset(mid.x - halfLength, mid.y), Offset(mid.x + halfLength, mid.y), strokeWidth = width)
            drawLine(color, Offset(mid.x, mid.y - halfLength), Offset(mid.x, mid.y + halfLength), strokeWidth = width)
        }
        cross((length.toPx() + 4.dp.toPx()) / 2, 4.dp.toPx(), Color.White)
        cross(length.toPx() / 2, 2.dp.toPx(), Color.Black)
    }
}

/** Something the Go map can do at the crosshair: a button while the map is dragged off the person's dot. */
class MapCrosshairAction(
    val id: String,
    val label: String,
    /** Returns words for the person, whether it worked or not. */
    val perform: suspend (latitude: Double, longitude: Double) -> String,
)

/** One "Set join location" per group the person organises with nearby join on. With several, each button names
 *  its group. */
fun joinLocationActions(
    groups: List<SessionMembership>,
    organiserActions: ((SessionMembership) -> GroupOrganiserActions)?,
): List<MapCrosshairAction> {
    if (organiserActions == null) return emptyList()
    val owned = groups.filter { it.isOwner && it.nearby == true }
    return owned.mapNotNull { group ->
        val set = organiserActions(group).setJoinLocationAt ?: return@mapNotNull null
        MapCrosshairAction(
            id = "join-location-${group.sessionId}",
            label = if (owned.size > 1) "Set join location for ${group.sessionName}" else "Set join location here",
            perform = { latitude, longitude ->
                set(latitude, longitude) ?: "Join location set for ${group.sessionName}"
            },
        )
    }
}

/** The organiser's join range for the Go map: the first group they organise with nearby on whose join location
 *  this phone knows. */
fun ownedGroupJoinRange(
    groups: List<SessionMembership>,
    organiserActions: ((SessionMembership) -> GroupOrganiserActions)?,
): JoinRange? {
    if (organiserActions == null) return null
    for (group in groups) {
        if (!group.isOwner || group.nearby != true) continue
        val actions = organiserActions(group)
        val point = actions.joinLocation ?: continue
        return JoinRange(point.latitude, point.longitude, actions.radiusMeters)
    }
    return null
}

/** The buttons under the crosshair on the Go map, and the result of the last one pressed. [centre] is the map's
 *  centre as (latitude, longitude). */
@Composable
fun MapCrosshairActionBar(
    actions: List<MapCrosshairAction>,
    centre: Pair<Double, Double>?,
    modifier: Modifier = Modifier,
) {
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()
    Column(
        modifier = modifier,
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(8.dp),
    ) {
        message?.let {
            MapChip { Text(it, fontWeight = FontWeight.SemiBold, color = Color.Black, textAlign = TextAlign.Center) }
        }
        actions.forEach { action ->
            MapChip(
                onClick = if (busy || centre == null) null else ({
                    val (latitude, longitude) = centre
                    scope.launch {
                        busy = true
                        message = action.perform(latitude, longitude)
                        busy = false
                        delay(4_000)
                        message = null
                    }
                }),
            ) {
                Text(if (busy) "Setting…" else action.label, fontWeight = FontWeight.SemiBold, color = Color.Black)
            }
        }
    }
}

/** A white rounded chip over the map, the same look as the Go map's group chip. */
@Composable
private fun MapChip(onClick: (() -> Unit)? = null, content: @Composable () -> Unit) {
    val shape = RoundedCornerShape(16.dp)
    Surface(
        modifier = Modifier
            .shadow(2.dp, shape)
            .clip(shape)
            .then(if (onClick != null) Modifier.clickable(onClick = onClick) else Modifier),
        shape = shape,
        color = Color.White.copy(alpha = 0.94f),
    ) {
        Box(Modifier.padding(horizontal = 14.dp, vertical = 9.dp)) { content() }
    }
}

/**
 * "Choose on map" from the organiser's group card: drag the map until the crosshair is on the spot, see the join
 * range around it, and set it. Opens on the current join location when this phone knows it, otherwise on where
 * the phone is. [onSet] runs with the words to show once it's set, then the dialog closes.
 */
@Composable
fun JoinLocationPickerDialog(
    group: SessionMembership,
    organiser: GroupOrganiserActions,
    onDismiss: () -> Unit,
    onSet: (String) -> Unit,
) {
    val setAt = organiser.setJoinLocationAt ?: return
    var centre by remember { mutableStateOf<Pair<Double, Double>?>(null) }
    // The first view, given to the map as an area to show whole once.
    var area by remember { mutableStateOf<List<RoutePoint>?>(null) }
    var areaTick by remember { mutableStateOf(0) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = rememberCoroutineScope()

    LaunchedEffect(Unit) {
        val start = organiser.joinLocation ?: organiser.currentPosition()
        area = if (start != null) {
            // Wide enough to see the whole range with room around it.
            val half = maxOf(0.003, organiser.radiusMeters * 2 / METRES_PER_DEGREE_LATITUDE)
            listOf(
                RoutePoint(start.latitude + half, start.longitude + half),
                RoutePoint(start.latitude - half, start.longitude - half),
            )
        } else {
            listOf(RoutePoint(-34.0, 166.0), RoutePoint(-47.5, 179.0)) // New Zealand
        }
        areaTick++
    }

    Dialog(onDismissRequest = onDismiss, properties = DialogProperties(usePlatformDefaultWidth = false)) {
        Surface(modifier = Modifier.fillMaxSize(), color = MaterialTheme.colorScheme.surface) {
            Column(
                modifier = Modifier.fillMaxSize().safeDrawingPadding().padding(20.dp),
                verticalArrangement = Arrangement.spacedBy(10.dp),
            ) {
                Text("Join location", style = MaterialTheme.typography.headlineSmall)
                Text(
                    "Move the map until the cross is where people should be to join ${group.sessionName}. " +
                        "You don't need to be there.",
                    style = MaterialTheme.typography.bodyMedium,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Box(
                    modifier = Modifier.fillMaxWidth().weight(1f).clip(RoundedCornerShape(12.dp)),
                    contentAlignment = Alignment.Center,
                ) {
                    MapScreen(
                        positions = emptyList(),
                        linzApiKey = BuildConfig.LINZ_API_KEY,
                        currentUserName = "",
                        fitAll = false,
                        joinRange = centre?.let { (latitude, longitude) ->
                            JoinRange(latitude, longitude, organiser.radiusMeters)
                        },
                        routeFocus = area,
                        routeFocusTick = areaTick,
                        onCenterChanged = { latitude, longitude -> centre = latitude to longitude },
                    )
                    MapCrosshair()
                }
                error?.let { Text(it, color = MaterialTheme.colorScheme.error, fontWeight = FontWeight.SemiBold) }
                Button(
                    onClick = {
                        val (latitude, longitude) = centre ?: return@Button
                        scope.launch {
                            busy = true
                            val failure = setAt(latitude, longitude)
                            busy = false
                            if (failure == null) {
                                onSet("Join location set on the map")
                                onDismiss()
                            } else {
                                error = failure
                            }
                        }
                    },
                    enabled = !busy && centre != null,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text(if (busy) "Setting…" else "Set join location") }
                TextButton(onClick = onDismiss, modifier = Modifier.fillMaxWidth()) { Text("Cancel") }
            }
        }
    }
}
