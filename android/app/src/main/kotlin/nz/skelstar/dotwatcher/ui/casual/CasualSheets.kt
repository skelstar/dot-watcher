package nz.skelstar.dotwatcher.ui.casual

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.Spacer
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.ExperimentalMaterial3Api
import androidx.compose.material3.FilterChip
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.ModalBottomSheet
import androidx.compose.material3.rememberModalBottomSheetState
import androidx.compose.foundation.layout.navigationBarsPadding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.OutlinedButton
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.text.SpanStyle
import androidx.compose.ui.text.buildAnnotatedString
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextDecoration
import androidx.compose.ui.text.withStyle
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.location.TRACKING_DURATION_OPTIONS
import java.time.Duration

/** How long a casual run shares for unless the person picks otherwise. Shorter than the 24 hours
 *  a session defaults to: a run is rarely longer than a few hours, and 24 is still one tap away. */
val DEFAULT_CASUAL_DURATION: Duration = Duration.ofHours(4)

/**
 * Shown every time Go is held, before anything starts. Says who will see the person, asks how long
 * to share for (sharing always stops on its own, the same choices as the session consent screen),
 * and offers a way to change who can see them.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun BeforeYouGoSheet(
    onGo: (Duration) -> Unit,
    onChooseWhoCanSeeMe: () -> Unit,
    onDismiss: () -> Unit,
) {
    var duration by remember { mutableStateOf(DEFAULT_CASUAL_DURATION) }

    ModalBottomSheet(
        onDismissRequest = onDismiss,
        sheetState = rememberModalBottomSheetState(skipPartiallyExpanded = true),
    ) {
        Column(
            modifier = Modifier
                .verticalScroll(rememberScrollState())
                .navigationBarsPadding()
                .padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Before you go", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            Point("Your crew see your name", "While you're out, with their colour ring on the map.")
            Point(
                "Nearby runners see nothing",
                "Unless you turn on Show me to people nearby. Then it's a grey dot with no name.",
            )
            Point("Stop any time", "Hold the red Stop, or it stops by itself when the time is up.")

            Column(verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(
                    text = "SHARE FOR",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                    TRACKING_DURATION_OPTIONS.forEach { option ->
                        FilterChip(
                            selected = option == duration,
                            onClick = { duration = option },
                            label = { Text("${option.toHours()}h") },
                        )
                    }
                }
                Text(
                    text = "Sharing always stops on its own. This picks how soon.",
                    style = MaterialTheme.typography.labelSmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant,
                )
            }

            Button(
                onClick = { onGo(duration) },
                colors = ButtonDefaults.buttonColors(containerColor = GoGreen),
                modifier = Modifier.fillMaxWidth().height(48.dp),
            ) {
                Text("Go", fontWeight = FontWeight.Bold)
            }
            OutlinedButton(onClick = onChooseWhoCanSeeMe, modifier = Modifier.fillMaxWidth()) {
                Text("Choose who can see me")
            }
        }
    }
}

@Composable
private fun Point(title: String, body: String) {
    Column {
        Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
        Text(body, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

/** "who aren't in your crew" is underlined: it is the group the setting is about. */
private val HIDE_START_AREA_BODY = buildAnnotatedString {
    append("People ")
    withStyle(SpanStyle(textDecoration = TextDecoration.Underline)) { append("who aren't in your crew") }
    append(" don't see you until you are 300 m from where you started.")
}

/**
 * Who can see a casual run. Crew always can. The switch controls strangers nearby: off until the
 * person turns it on, then remembered. Only people who have also turned it on can be seen, and
 * they see each other, so turning it off also stops showing them others.
 */
@OptIn(ExperimentalMaterial3Api::class)
@Composable
fun WhoCanSeeMeSheet(
    visibleToNonCrew: Boolean,
    hideStartArea: Boolean,
    error: String?,
    onVisibleChange: (Boolean) -> Unit,
    onHideStartAreaChange: (Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    ModalBottomSheet(onDismissRequest = onDismiss) {
        Column(
            modifier = Modifier.navigationBarsPadding().padding(start = 20.dp, end = 20.dp, bottom = 28.dp),
            verticalArrangement = Arrangement.spacedBy(14.dp),
        ) {
            Text("Who can see me", style = MaterialTheme.typography.headlineSmall, fontWeight = FontWeight.Bold)
            SwitchRow(
                title = "My crew",
                body = AnnotatedString("Always. They see your name."),
                checked = true,
                enabled = false,
                onCheckedChange = {},
            )
            SwitchRow(
                title = "Show me to people nearby",
                body = AnnotatedString("A grey dot with no name, within 5 km."),
                checked = visibleToNonCrew,
                enabled = true,
                onCheckedChange = onVisibleChange,
            )
            SwitchRow(
                title = "Hide my start area",
                body = HIDE_START_AREA_BODY,
                checked = hideStartArea,
                enabled = visibleToNonCrew,
                onCheckedChange = onHideStartAreaChange,
            )
            if (visibleToNonCrew && !hideStartArea) {
                Text(
                    text = "Anyone nearby can see where you set off from, which may be your home.",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.error,
                )
            }
            Text(
                text = "Only people who have also turned on \"Show me to people nearby\" can be seen, and you " +
                    "see them. Your choices are remembered next time.",
                style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
            )
            if (error != null) {
                Text(error, color = MaterialTheme.colorScheme.error, style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}

@Composable
private fun SwitchRow(
    title: String,
    body: AnnotatedString,
    checked: Boolean,
    enabled: Boolean,
    onCheckedChange: (Boolean) -> Unit,
) {
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(title, style = MaterialTheme.typography.titleSmall, fontWeight = FontWeight.SemiBold)
            Text(body, style = MaterialTheme.typography.bodySmall, color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Spacer(Modifier.width(12.dp))
        Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
    }
}
