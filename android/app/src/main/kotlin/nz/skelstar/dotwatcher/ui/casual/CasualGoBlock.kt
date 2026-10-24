package nz.skelstar.dotwatcher.ui.casual

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.dp

/** The line under the crew row: who is out, or that nobody is. null while still loading. */
fun crewOutLine(crewOut: Int?): String? = when (crewOut) {
    null -> null
    0 -> "No crew out right now. They'll show up here."
    1 -> "1 of your crew is out now"
    else -> "$crewOut of your crew are out now"
}

/**
 * The start screen's casual block: the big hold-to-go button with its caption. Holding Go opens
 * "Before you go", which is where tracking actually starts. Shown only when the server has casual
 * for this account, so an app talking to an older server looks as it did.
 */
@Composable
fun CasualGoBlock(crewOut: Int?, onGoHeld: () -> Unit, modifier: Modifier = Modifier) {
    Column(
        modifier = modifier.fillMaxWidth(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.spacedBy(10.dp),
    ) {
        crewOutLine(crewOut)?.let {
            Text(
                text = it,
                style = MaterialTheme.typography.bodyMedium,
                color = MaterialTheme.colorScheme.onSurfaceVariant,
                textAlign = TextAlign.Center,
            )
        }
        HoldToGoButton(onComplete = onGoHeld, modifier = Modifier.padding(vertical = 4.dp))
        Text(
            text = "HOLD TO GO",
            style = MaterialTheme.typography.labelMedium,
            fontWeight = FontWeight.Bold,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
        )
        Text(
            text = "Out for a run? Hold Go. Your crew will see you.",
            style = MaterialTheme.typography.bodyMedium,
            color = MaterialTheme.colorScheme.onSurfaceVariant,
            textAlign = TextAlign.Center,
        )
    }
}
