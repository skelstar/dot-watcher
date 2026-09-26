package nz.skelstar.dotwatcher

import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.lifecycle.viewmodel.compose.viewModel
import nz.skelstar.dotwatcher.ui.auth.AuthScreen
import nz.skelstar.dotwatcher.ui.map.LiveMapScreen
import nz.skelstar.dotwatcher.ui.session.SessionScreen
import nz.skelstar.dotwatcher.ui.theme.DotWatcherTheme

/**
 * Milestone 1's whole flow: sign in -> create/join a session -> live map. A single
 * [Destination] state (owned by [AppViewModel]) drives which screen shows, rather than
 * androidx.navigation — the flow is strictly linear with no back-stack needs yet. Revisit if
 * Milestone 3 needs real back navigation (e.g. a session list with per-session detail).
 */
@Composable
fun DotWatcherApp(hasLocationPermission: Boolean) {
    val viewModel: AppViewModel = viewModel()
    val destination by viewModel.destination.collectAsState()
    val authState by viewModel.authState.collectAsState()
    val sessionState by viewModel.sessionState.collectAsState()

    DotWatcherTheme {
        Surface(modifier = Modifier.fillMaxSize()) {
            when (val current = destination) {
                Destination.Auth -> AuthScreen(
                    state = authState,
                    onRegister = viewModel::register,
                    onLogin = viewModel::login,
                )

                Destination.Session -> SessionScreen(
                    state = sessionState,
                    onCreateSession = viewModel::createSession,
                    onJoinSession = viewModel::joinSession,
                    onSignOut = viewModel::signOut,
                )

                is Destination.Map -> LiveMapScreen(
                    membership = current.membership,
                    repository = viewModel.repository,
                    hasLocationPermission = hasLocationPermission,
                    onLeaveSession = viewModel::returnToSessionPicker,
                )
            }
        }
    }
}
