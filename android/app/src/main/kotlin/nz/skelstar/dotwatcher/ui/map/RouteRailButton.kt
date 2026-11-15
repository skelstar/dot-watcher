package nz.skelstar.dotwatcher.ui.map

import android.content.Context
import android.net.Uri
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import nz.skelstar.dotwatcher.route.NO_TRACK_MESSAGE
import nz.skelstar.dotwatcher.route.ROUTE_MAX_BYTES
import nz.skelstar.dotwatcher.route.RoutePoint
import nz.skelstar.dotwatcher.route.isDrawable
import nz.skelstar.dotwatcher.route.parseGpxTrack
import java.io.ByteArrayOutputStream

/**
 * What the route button does on a given map, like iOS's RouteActions: on the session map the
 * session's one route, on the casual map this person's own. [upload] and [remove] return an error
 * message, or null when the server took the change.
 */
class RouteActions(
    val hasRoute: Boolean,
    /** Shown in the "Remove route?" confirmation: who loses the route. */
    val removeMessage: String,
    val upload: suspend (gpx: ByteArray) -> String?,
    val remove: suspend () -> String?,
)

/**
 * The rail's route button: a menu with "Add route (GPX)…" (or "Replace route (GPX)…") and, when there
 * is one, "Remove route". Adding opens the system file picker; the file is checked for a track here,
 * so a file with none gets a clear message without a round trip, then uploaded. [onRouteAdded] gets
 * the new route's points so the map can show it whole.
 */
@Composable
internal fun RouteRailButton(actions: RouteActions, onRouteAdded: (List<RoutePoint>) -> Unit) {
    val context = LocalContext.current
    val scope = rememberCoroutineScope()
    var menuOpen by remember { mutableStateOf(false) }
    var confirmRemove by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var error by remember { mutableStateOf<String?>(null) }

    // Any type: phones rarely know .gpx by name (often application/octet-stream), so the file is
    // judged by whether it has a track rather than by its type, as on iOS (.gpx or any XML).
    val picker = rememberLauncherForActivityResult(ActivityResultContracts.OpenDocument()) { uri ->
        if (uri == null) return@rememberLauncherForActivityResult
        busy = true
        scope.launch {
            try {
                val gpx = withContext(Dispatchers.IO) { context.readRouteFile(uri) }
                if (gpx == null) {
                    error = "Couldn't read that file."
                    return@launch
                }
                if (gpx.size > ROUTE_MAX_BYTES) {
                    error = "That file is too big for a route."
                    return@launch
                }
                val points = withContext(Dispatchers.Default) { parseGpxTrack(gpx) }
                if (!points.isDrawable()) {
                    error = NO_TRACK_MESSAGE
                    return@launch
                }
                val failure = actions.upload(gpx)
                if (failure != null) error = failure else onRouteAdded(points)
            } finally {
                busy = false
            }
        }
    }

    Box {
        RailButton(RailIcon.Route, "Route") { if (!busy) menuOpen = true }
        if (busy) {
            CircularProgressIndicator(
                modifier = Modifier.size(44.dp).align(Alignment.Center),
                strokeWidth = 2.dp,
            )
        }
        DropdownMenu(expanded = menuOpen, onDismissRequest = { menuOpen = false }) {
            DropdownMenuItem(
                text = { Text(if (actions.hasRoute) "Replace route (GPX)…" else "Add route (GPX)…") },
                onClick = {
                    menuOpen = false
                    picker.launch(arrayOf("*/*"))
                },
            )
            if (actions.hasRoute) {
                DropdownMenuItem(
                    text = { Text("Remove route", color = MaterialTheme.colorScheme.error) },
                    onClick = {
                        menuOpen = false
                        confirmRemove = true
                    },
                )
            }
        }
    }

    if (confirmRemove) {
        AlertDialog(
            onDismissRequest = { confirmRemove = false },
            title = { Text("Remove route?") },
            text = { Text(actions.removeMessage) },
            confirmButton = {
                TextButton(onClick = {
                    confirmRemove = false
                    busy = true
                    scope.launch {
                        try {
                            actions.remove()?.let { error = it }
                        } finally {
                            busy = false
                        }
                    }
                }) { Text("Remove", color = MaterialTheme.colorScheme.error) }
            },
            dismissButton = { TextButton(onClick = { confirmRemove = false }) { Text("Cancel") } },
        )
    }

    error?.let { message ->
        AlertDialog(
            onDismissRequest = { error = null },
            title = { Text("Couldn't update the route") },
            text = { Text(message) },
            confirmButton = { TextButton(onClick = { error = null }) { Text("OK") } },
        )
    }
}

/** The picked file's bytes, stopping one byte past [ROUTE_MAX_BYTES] so a huge file isn't read whole;
 *  null when it can't be opened. */
private fun Context.readRouteFile(uri: Uri): ByteArray? = runCatching {
    contentResolver.openInputStream(uri)?.use { input ->
        val out = ByteArrayOutputStream()
        val buffer = ByteArray(64 * 1024)
        while (out.size() <= ROUTE_MAX_BYTES) {
            val read = input.read(buffer)
            if (read < 0) break
            out.write(buffer, 0, read)
        }
        out.toByteArray()
    }
}.getOrNull()
