package nz.skelstar.dotwatcher.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.size
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

    BasicTextField(
        value = value,
        onValueChange = { new -> onValueChange(sanitizeCode(new, length, lettersOnly)) },
        modifier = modifier.onFocusChanged { isFocused = it.isFocused },
        textStyle = TextStyle(color = Color.Transparent),
        keyboardOptions = KeyboardOptions(
            capitalization = KeyboardCapitalization.Characters,
            autoCorrectEnabled = false,
            keyboardType = KeyboardType.Ascii,
        ),
        cursorBrush = SolidColor(Color.Transparent),
        decorationBox = {
            Row(horizontalArrangement = Arrangement.spacedBy(6.dp)) {
                for (i in 0 until length) {
                    val char = value.getOrNull(i)?.toString() ?: ""
                    val isActive = isFocused && value.length == i
                    CodeBox(char = char, isActive = isActive)
                }
            }
        },
    )
}

@Composable
private fun CodeBox(char: String, isActive: Boolean) {
    Box(
        modifier = Modifier
            .size(width = 36.dp, height = 44.dp)
            .background(MaterialTheme.colorScheme.surfaceVariant, RoundedCornerShape(8.dp))
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
