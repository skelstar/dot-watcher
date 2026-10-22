package nz.skelstar.dotwatcher.ui.crew

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.crew.CrewConfirm
import nz.skelstar.dotwatcher.crew.CrewModel
import nz.skelstar.dotwatcher.crew.label

/**
 * "Add Priya to your crew? They'll see you too." The one place anyone is added: a crew link, a
 * pasted code and a typed code all end here, and nothing happens until the person taps Add. Shown
 * at the app level (over whatever screen is up) because a link can arrive while on the map.
 */
@Composable
fun CrewConfirmDialog(confirm: CrewConfirm, crew: CrewModel) {
    when (confirm) {
        is CrewConfirm.Loading, is CrewConfirm.Adding -> AlertDialog(
            onDismissRequest = {},
            confirmButton = {},
            title = { Text(if (confirm is CrewConfirm.Loading) "Checking the code…" else "Adding to your crew…") },
            text = {
                Box(modifier = Modifier.fillMaxWidth().padding(8.dp), contentAlignment = Alignment.Center) {
                    CircularProgressIndicator()
                }
            },
        )

        is CrewConfirm.Ready -> {
            val name = confirm.owner.label()
            if (confirm.owner.alreadyCrew) {
                AlertDialog(
                    onDismissRequest = crew::dismissConfirm,
                    title = { Text("$name is already in your crew") },
                    text = { Text("You don't need to use this code.") },
                    confirmButton = { TextButton(onClick = crew::dismissConfirm) { Text("OK") } },
                )
            } else {
                AlertDialog(
                    onDismissRequest = crew::dismissConfirm,
                    title = { Text("Add $name to your crew?") },
                    text = { Text("They'll see you in their crew too. You can each remove the other at any time.") },
                    confirmButton = { TextButton(onClick = crew::confirmAdd) { Text("Add") } },
                    dismissButton = { TextButton(onClick = crew::dismissConfirm) { Text("Cancel") } },
                )
            }
        }

        is CrewConfirm.Done -> AlertDialog(
            onDismissRequest = crew::dismissConfirm,
            title = { Text("${confirm.member.label()} is in your crew") },
            confirmButton = { TextButton(onClick = crew::dismissConfirm) { Text("OK") } },
        )

        is CrewConfirm.Failed -> AlertDialog(
            onDismissRequest = crew::dismissConfirm,
            title = { Text("Couldn't add them") },
            text = { Text(confirm.message) },
            confirmButton = { TextButton(onClick = crew::dismissConfirm) { Text("OK") } },
        )
    }
}
