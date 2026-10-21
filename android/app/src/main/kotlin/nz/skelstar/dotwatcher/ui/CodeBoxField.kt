package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.BasicTextField
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.LocalTextStyle
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.SolidColor
import androidx.compose.ui.text.TextStyle
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp

/**
 * "Wordle-style" per-character boxed input: one bordered square per character, filled from left
 * to right, with the currently-focused/next-to-fill box highlighted. Ported from
 * ios/DotWatcher/DotWatcher/CodeBoxField.swift, including its two call shapes — invite/session
 * codes (`lettersOnly = false`: letters + digits) and 2-letter runner initials (`lettersOnly =
 * true`) — and its paste-handling: pasting a whole shared message (e.g. from WhatsApp) extracts
 * the first matching-length word rather than dumping the whole string in, preferring a
 * hex-looking word since invite/session codes are hex, to avoid matching a coincidentally
 * same-length ordinary word.
 *
 * Uses the same invisible-real-input-over-drawn-boxes technique as the SwiftUI original: a
 * transparent [BasicTextField] receives all real keyboard/paste input via [decorationBox], while
 * the boxes below just render [value]'s characters.
 */
@Composable
fun CodeBoxField(
    value: String,
    onValueChange: (String) -> Unit,
    length: Int = 6,
    lettersOnly: Boolean = false,
    modifier: Modifier = Modifier,
) {
    var isFocused by remember { mutableStateOf(false) }
    val spacing = 6.dp

    // Box width shrinks to fit whatever space is available (e.g. a narrower dialog hosting an
    // 8-box field) rather than a fixed 36.dp that can overflow its container — capped at 36.dp so
    // a roomy container (the 6-box invite-code card) still gets the original comfortable size.
    BoxWithConstraints(modifier = modifier) {
        val boxWidth = minOf(36.dp, (maxWidth - spacing * (length - 1)) / length)

        BasicTextField(
            value = value,
            onValueChange = { new -> onValueChange(sanitizeCode(new, length, lettersOnly)) },
            modifier = Modifier.onFocusChanged { isFocused = it.isFocused },
            textStyle = TextStyle(color = Color.Transparent),
            keyboardOptions = KeyboardOptions(
                capitalization = KeyboardCapitalization.Characters,
                autoCorrectEnabled = false,
                keyboardType = KeyboardType.Ascii,
            ),
            cursorBrush = SolidColor(Color.Transparent),
            decorationBox = {
                Row(horizontalArrangement = Arrangement.spacedBy(spacing)) {
                    for (i in 0 until length) {
                        val char = value.getOrNull(i)?.toString() ?: ""
                        val isActive = isFocused && value.length == i
                        CodeBox(char = char, isActive = isActive, width = boxWidth)
                    }
                }
            },
        )
    }
}

@Composable
private fun CodeBox(char: String, isActive: Boolean, width: Dp) {
    Box(
        modifier = Modifier
            .width(width)
            .height(44.dp)
            // Matches iOS's white boxes-on-gray-card look (media-files/Simulator Screenshot ...
            // 16.19.49.png) — `colorScheme.surface` rather than `surfaceVariant`, which is now
            // close enough to GroupedCard's own gray fill (ui/GroupedCard.kt) that the boxes
            // would barely stand out against it.
            .background(MaterialTheme.colorScheme.surface, RoundedCornerShape(8.dp))
            .border(
                width = if (isActive) 3.dp else 1.dp,
                color = if (isActive) MaterialTheme.colorScheme.onSurface else MaterialTheme.colorScheme.outline,
                shape = RoundedCornerShape(8.dp),
            ),
        contentAlignment = Alignment.Center,
    ) {
        Text(
            text = char,
            style = LocalTextStyle.current.copy(
                fontSize = 20.sp,
                fontWeight = FontWeight.Bold,
                fontFamily = FontFamily.Monospace,
                textAlign = TextAlign.Center,
            ),
        )
    }
}

// Typing produces valid input incrementally, so a plain filter+truncate is enough there. Pasting
// (e.g. a whole WhatsApp share message with the code embedded in a sentence) can carry
// surrounding words, so look for a whole word of exactly `length` matching characters rather
// than just taking the first matching characters in sequence. Invite/session codes are hex
// (0-9A-F), which ordinary English words practically never are, so prefer a hex-only word when
// one exists to avoid matching a coincidentally-same-length word like "INVITE" (8 letters).
private fun sanitizeCode(raw: String, length: Int, lettersOnly: Boolean): String {
    val isMatch: (Char) -> Boolean = if (lettersOnly) {
        { (it.isLetter() && it.code < 128) || it.isDigit() }
    } else {
        { it.isLetter() || it.isDigit() }
    }

    val uppercased = raw.uppercase()
    if (uppercased.length <= length) {
        return uppercased.filter(isMatch).take(length)
    }

    val words = uppercased.split(Regex("[^A-Z0-9]+")).filter { it.length == length }
    val isHexDigit: (Char) -> Boolean = { it.isDigit() || it in 'A'..'F' }
    val hexWord = words.firstOrNull { word -> word.all(isHexDigit) }
    if (hexWord != null) return hexWord
    val firstWord = words.firstOrNull()
    if (firstWord != null) return firstWord
    return uppercased.filter(isMatch).take(length)
}
