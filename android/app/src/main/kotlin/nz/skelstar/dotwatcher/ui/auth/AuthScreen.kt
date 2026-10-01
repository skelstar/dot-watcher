package nz.skelstar.dotwatcher.ui.auth

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.SegmentedButton
import androidx.compose.material3.SegmentedButtonDefaults
import androidx.compose.material3.SingleChoiceSegmentedButtonRow
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextField
import androidx.compose.material3.TextFieldDefaults
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.text.input.VisualTransformation
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.AuthUiState
import nz.skelstar.dotwatcher.ui.GroupDivider
import nz.skelstar.dotwatcher.ui.GroupedCard

/**
 * Sign-in/sign-up screen: [POST /auth/register] and [POST /auth/login]
 * (repo root README.md, "Auth model"). A runner needs an account before they can create or
 * join a session — this is always the first screen for a signed-out user.
 *
 * Laid out to match the real iOS "Account" screen (media-files/Simulator Screenshot ... 16.18.44
 * / 16.19.15.png): a large left-aligned title, a segmented Sign In/Create toggle at the top of a
 * grouped card, borderless fields with dividers between them inside that card, then a full-width
 * action button below the card.
 */
@Composable
fun AuthScreen(
    state: AuthUiState,
    onRegister: (username: String, password: String, displayName: String) -> Unit,
    onLogin: (username: String, password: String) -> Unit,
) {
    var isRegisterMode by remember { mutableStateOf(false) }
    var username by remember { mutableStateOf("") }
    var password by remember { mutableStateOf("") }
    var displayName by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .padding(24.dp),
    ) {
        Spacer(modifier = Modifier.height(32.dp))
        Text(text = "Account", style = MaterialTheme.typography.headlineLarge)
        Spacer(modifier = Modifier.height(24.dp))

        GroupedCard(modifier = Modifier.fillMaxWidth()) {
            // Inactive segment colors match the card's own gray fill (rather than Material3's
            // default `colorScheme.surface`, an off-white that would paint a visibly
            // mismatched rectangle over the card) — only the active segment stands out as white,
            // matching the iOS screenshot's toggle.
            val segmentColors = SegmentedButtonDefaults.colors(
                inactiveContainerColor = Color.Transparent,
                disabledInactiveContainerColor = Color.Transparent,
            )
            SingleChoiceSegmentedButtonRow(
                modifier = Modifier
                    .fillMaxWidth()
                    .padding(12.dp),
            ) {
                SegmentedButton(
                    selected = !isRegisterMode,
                    onClick = { isRegisterMode = false },
                    shape = SegmentedButtonDefaults.itemShape(index = 0, count = 2),
                    colors = segmentColors,
                ) {
                    Text("Sign In")
                }
                SegmentedButton(
                    selected = isRegisterMode,
                    onClick = { isRegisterMode = true },
                    shape = SegmentedButtonDefaults.itemShape(index = 1, count = 2),
                    colors = segmentColors,
                ) {
                    Text("Create")
                }
            }
            GroupDivider()

            BorderlessField(
                value = username,
                onValueChange = { username = it },
                placeholder = "Username",
            )
            GroupDivider()
            BorderlessField(
                value = password,
                onValueChange = { password = it },
                placeholder = "Password",
                visualTransformation = PasswordVisualTransformation(),
            )

            if (isRegisterMode) {
                GroupDivider()
                BorderlessField(
                    value = displayName,
                    onValueChange = { displayName = it },
                    placeholder = "Display name",
                )
            }
        }

        Spacer(modifier = Modifier.height(16.dp))

        when (state) {
            is AuthUiState.Loading -> CircularProgressIndicator(modifier = Modifier.padding(bottom = 16.dp))
            is AuthUiState.Error -> Text(
                text = state.message,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(bottom = 16.dp),
            )
            AuthUiState.Idle -> Unit
        }

        GroupedCard(modifier = Modifier.fillMaxWidth()) {
            CardActionRow(
                text = if (isRegisterMode) "Create Account" else "Sign In",
                enabled = state !is AuthUiState.Loading && username.isNotBlank() && password.isNotBlank() &&
                    (!isRegisterMode || displayName.isNotBlank()),
                onClick = {
                    if (isRegisterMode) {
                        onRegister(username, password, displayName)
                    } else {
                        onLogin(username, password)
                    }
                },
            )
        }
    }
}

/** A field with no visible border/outline/fill, matching the iOS screenshot's plain
 *  underlined-row look inside a [GroupedCard] — a fully transparent container so the card's own
 *  background shows through, rather than Material3's [TextField] default container color (which
 *  would otherwise paint a visibly different-colored rectangle over the card's fill). */
@Composable
private fun BorderlessField(
    value: String,
    onValueChange: (String) -> Unit,
    placeholder: String,
    visualTransformation: VisualTransformation = VisualTransformation.None,
) {
    TextField(
        value = value,
        onValueChange = onValueChange,
        placeholder = { Text(placeholder) },
        singleLine = true,
        visualTransformation = visualTransformation,
        colors = TextFieldDefaults.colors(
            focusedContainerColor = Color.Transparent,
            unfocusedContainerColor = Color.Transparent,
            focusedIndicatorColor = Color.Transparent,
            unfocusedIndicatorColor = Color.Transparent,
        ),
        modifier = Modifier.fillMaxWidth(),
    )
}

/** A left-aligned text "button" styled as a plain row, matching iOS's blue-text action rows
 *  (e.g. "Create Account", "Privacy Policy") inside a grouped card, rather than Material3's
 *  pill-shaped [androidx.compose.material3.Button]. */
@Composable
private fun CardActionRow(text: String, enabled: Boolean, onClick: () -> Unit) {
    TextButton(
        onClick = onClick,
        enabled = enabled,
        modifier = Modifier.fillMaxWidth(),
    ) {
        Text(
            text = text,
            modifier = Modifier
                .fillMaxWidth()
                .padding(vertical = 4.dp),
        )
    }
}
