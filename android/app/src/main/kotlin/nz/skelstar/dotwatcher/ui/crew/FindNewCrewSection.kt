package nz.skelstar.dotwatcher.ui.crew

import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.background
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.hapticfeedback.HapticFeedbackType
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.platform.LocalHapticFeedback
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.delay
import nz.skelstar.dotwatcher.crew.CrewFindingUi
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.crew.label
import nz.skelstar.dotwatcher.network.CrewNearbyPerson
import nz.skelstar.dotwatcher.ui.CrewDot

private val AcceptedGreen = Color(0xFF1B8A3A)
private val WaitingAmber = Color(0xFFB26A00)

/**
 * "Find new crew", at the top of the crew sheet: the switch with its countdown, the people next to you who
 * also have it on, and the Accept question. See [CrewModel.startFinding] and .ai/plans/crew-finding.md.
 */
@Composable
fun FindNewCrewSection(crew: CrewModel, finding: CrewFindingUi) {
    val context = LocalContext.current
    val haptics = LocalHapticFeedback.current
    var candidate by remember { mutableStateOf<CrewNearbyPerson?>(null) }

    // A light buzz when someone new accepts you, and when someone becomes crew.
    val acceptedYouIds = finding.people.filter { it.acceptedYou }.map { it.id }.toSet()
    var buzzedFor by remember { mutableStateOf(emptySet<String>()) }
    LaunchedEffect(acceptedYouIds) {
        if ((acceptedYouIds - buzzedFor).isNotEmpty()) haptics.performHapticFeedback(HapticFeedbackType.TextHandleMove)
        buzzedFor = acceptedYouIds
    }
    val justAdded = finding.justAdded
    LaunchedEffect(justAdded?.userId) {
        if (justAdded == null) return@LaunchedEffect
        haptics.performHapticFeedback(HapticFeedbackType.LongPress)
        delay(4_000)
        crew.clearJustAdded()
    }

    // Ticks the countdown once a second while it's on.
    var now by remember { mutableStateOf(System.currentTimeMillis()) }
    LaunchedEffect(finding.isOn) {
        while (finding.isOn) {
            now = System.currentTimeMillis()
            delay(1_000)
        }
    }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(14.dp))
            .padding(14.dp),
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Column(modifier = Modifier.weight(1f)) {
                Text("Find new crew", style = MaterialTheme.typography.titleMedium)
                val expiresAt = finding.expiresAtMillis
                Text(
                    text = when {
                        finding.isStarting -> "Turning on…"
                        finding.isOn && expiresAt != null -> "On for " + countdown(expiresAt - now)
                        else -> "Let people right next to you add you"
                    },
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }
            Switch(
                checked = finding.isOn || finding.isStarting,
                onCheckedChange = { crew.setFinding(it) },
                enabled = !finding.isStarting,
            )
        }

        finding.note?.let { note ->
            Text(note, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
            if (finding.needsSettings) {
                TextButton(onClick = {
                    context.startActivity(
                        Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.fromParts("package", context.packageName, null))
                            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK),
                    )
                }) { Text("Open Settings") }
            }
        }

        justAdded?.let {
            Text(
                text = "✓ ${it.label()} is in your crew",
                style = MaterialTheme.typography.bodyMedium,
                fontWeight = FontWeight.SemiBold,
                color = AcceptedGreen,
            )
        }

        if (finding.isOn) {
            Row(verticalAlignment = Alignment.CenterVertically) {
                CircularProgressIndicator(modifier = Modifier.size(14.dp), strokeWidth = 2.dp)
                Spacer(modifier = Modifier.width(8.dp))
                Text(
                    "Looking for people within 50 m",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            if (finding.people.isEmpty()) {
                Text(
                    "No one yet. Ask them to turn on Find new crew too.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            } else {
                Column(
                    modifier = Modifier
                        .fillMaxWidth()
                        .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(12.dp)),
                ) {
                    finding.people.forEachIndexed { index, person ->
                        if (index > 0) HorizontalDivider(modifier = Modifier.padding(start = 52.dp))
                        PersonRow(
                            person = person,
                            accepting = finding.acceptingId == person.id,
                            enabled = !person.youAccepted && finding.acceptingId == null,
                            onClick = { candidate = person },
                        )
                    }
                }
            }

            finding.acceptError?.let {
                Text(it, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.error)
            }

            Text(
                "Only people who also have this on. They see your name too, nothing else.",
                style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
    }

    candidate?.let { person ->
        AlertDialog(
            onDismissRequest = { candidate = null },
            title = { Text("Add ${person.label()} to your crew?") },
            text = {
                Text(
                    if (person.acceptedYou) {
                        "${person.label()} has already accepted you. You'll see each other on the map."
                    } else {
                        "You'll see each other on the map. You'll be crew once ${person.label()} accepts you as well."
                    },
                )
            },
            confirmButton = {
                TextButton(onClick = {
                    candidate = null
                    crew.acceptNearby(person)
                }) { Text("Accept") }
            },
            dismissButton = {
                TextButton(onClick = { candidate = null }) { Text(if (person.acceptedYou) "Not now" else "Cancel") }
            },
        )
    }
}

@Composable
private fun PersonRow(person: CrewNearbyPerson, accepting: Boolean, enabled: Boolean, onClick: () -> Unit) {
    Row(
        verticalAlignment = Alignment.CenterVertically,
        modifier = Modifier
            .fillMaxWidth()
            .clickable(enabled = enabled, onClick = onClick)
            .padding(horizontal = 12.dp, vertical = 9.dp),
    ) {
        CrewDot(initials = person.displayName, color = Color.Gray, size = 30.dp, isCrew = false)
        Spacer(modifier = Modifier.width(10.dp))
        Column(modifier = Modifier.weight(1f)) {
            Text(person.label(), style = MaterialTheme.typography.bodyMedium, fontWeight = FontWeight.SemiBold)
            Text(
                text = when {
                    person.acceptedYou -> "Accepted you"
                    person.youAccepted -> "Waiting for ${person.label()}"
                    else -> "Tap to add"
                },
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
        }
        when {
            accepting -> CircularProgressIndicator(modifier = Modifier.size(18.dp), strokeWidth = 2.dp)
            person.acceptedYou -> Badge("Wants you as crew", AcceptedGreen)
            person.youAccepted -> Badge("Waiting", WaitingAmber)
            else -> Text("›", color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

@Composable
private fun Badge(text: String, color: Color) {
    Text(
        text = text,
        style = MaterialTheme.typography.labelSmall,
        fontWeight = FontWeight.Bold,
        color = color,
        modifier = Modifier
            .background(color.copy(alpha = 0.15f), RoundedCornerShape(6.dp))
            .padding(horizontal = 6.dp, vertical = 2.dp),
    )
}

/** "1:48". */
internal fun countdown(remainingMillis: Long): String {
    val seconds = ((remainingMillis.coerceAtLeast(0) + 999) / 1000).toInt()
    return "%d:%02d".format(seconds / 60, seconds % 60)
}
