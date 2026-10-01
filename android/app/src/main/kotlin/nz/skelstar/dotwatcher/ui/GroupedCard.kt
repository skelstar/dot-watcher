package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/** iOS's `UIColor.systemGray5` — the exact fill color its grouped-list cards use (sampled
 *  directly from media-files/Simulator Screenshot ... 16.19.49.png: `#E5E5EA`), not a
 *  Material3-derived approximation. `#2C2C2E` is Apple's documented dark-mode counterpart. */
private val GroupedCardLight = Color(0xFFE5E5EA)
private val GroupedCardDark = Color(0xFF2C2C2E)

/**
 * A rounded, tinted-background section grouping related rows with thin dividers between them —
 * matches the iOS app's grouped-list look seen throughout its screens (the "Account" screen's
 * User/Initials/Sign out card, the "Join a Session" card, "Recent Sessions" card — see
 * media-files/Simulator Screenshot ... .png for reference), which Material3's defaults (plain
 * `OutlinedTextField`s floating on the page background) didn't have any equivalent of.
 */
@Composable
fun GroupedCard(
    modifier: Modifier = Modifier,
    content: @Composable ColumnScope.() -> Unit,
) {
    val cardColor = if (isSystemInDarkTheme()) GroupedCardDark else GroupedCardLight
    Column(
        modifier = modifier
            .background(cardColor, RoundedCornerShape(14.dp))
            .padding(vertical = 4.dp),
        content = content,
    )
}

/** A divider matching [GroupedCard]'s row-separator styling — inset to align with row content
 *  padding rather than running edge-to-edge. */
@Composable
fun GroupDivider() {
    HorizontalDivider(
        modifier = Modifier.padding(horizontal = 16.dp),
        color = MaterialTheme.colorScheme.outline.copy(alpha = 0.3f),
    )
}
