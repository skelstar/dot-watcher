package nz.skelstar.dotwatcher.ui.auth

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.PasswordVisualTransformation
import androidx.compose.ui.unit.dp
import nz.skelstar.dotwatcher.AuthUiState

/**
 * Sign-in/sign-up screen: [POST /auth/register] and [POST /auth/login]
 * (repo root README.md, "Auth model"). A runner needs an account before they can create or
 * join a session — this is always the first screen for a signed-out user.
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
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(text = "Dot Watcher", style = MaterialTheme.typography.headlineMedium)
        Text(
            text = if (isRegisterMode) "Create an account" else "Sign in",
            style = MaterialTheme.typography.bodyMedium,
            modifier = Modifier.padding(top = 8.dp, bottom = 24.dp),
        )

        OutlinedTextField(
            value = username,
            onValueChange = { username = it },
            label = { Text("Username") },
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(8.dp))

        OutlinedTextField(
            value = password,
            onValueChange = { password = it },
            label = { Text("Password") },
            singleLine = true,
            visualTransformation = PasswordVisualTransformation(),
            modifier = Modifier.fillMaxWidth(),
        )

        if (isRegisterMode) {
            Spacer(modifier = Modifier.height(8.dp))
            OutlinedTextField(
                value = displayName,
                onValueChange = { displayName = it },
                label = { Text("Display name") },
                singleLine = true,
                modifier = Modifier.fillMaxWidth(),
            )
        }

        Spacer(modifier = Modifier.height(16.dp))

        when (state) {
            is AuthUiState.Loading -> CircularProgressIndicator()
            is AuthUiState.Error -> Text(
                text = state.message,
                color = MaterialTheme.colorScheme.error,
                modifier = Modifier.padding(bottom = 8.dp),
            )
            AuthUiState.Idle -> Unit
        }

        Button(
            onClick = {
                if (isRegisterMode) {
                    onRegister(username, password, displayName)
                } else {
                    onLogin(username, password)
                }
            },
            enabled = state !is AuthUiState.Loading && username.isNotBlank() && password.isNotBlank() &&
                (!isRegisterMode || displayName.isNotBlank()),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(if (isRegisterMode) "Create account" else "Sign in")
        }

        TextButton(onClick = { isRegisterMode = !isRegisterMode }) {
            Text(
                if (isRegisterMode) "Already have an account? Sign in"
                else "New here? Create an account"
            )
        }
    }
}
