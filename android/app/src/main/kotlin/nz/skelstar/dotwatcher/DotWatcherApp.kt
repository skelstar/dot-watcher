package nz.skelstar.dotwatcher

import android.content.Intent
import android.net.Uri
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.viewmodel.compose.viewModel
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.network.UpdateRequiredState
import nz.skelstar.dotwatcher.ui.UpdateRequiredScreen
import nz.skelstar.dotwatcher.ui.auth.AuthScreen
import nz.skelstar.dotwatcher.ui.map.LiveMapScreen
import nz.skelstar.dotwatcher.ui.map.ShareLocationConsentScreen
import nz.skelstar.dotwatcher.ui.session.SessionScreen
import nz.skelstar.dotwatcher.ui.theme.DotWatcherTheme

/**
 * The whole flow: sign in -> create/join a session -> (runners only) share-location consent ->
 * live map. A single [Destination] state (owned by [AppViewModel]) drives which screen shows,
 * rather than androidx.navigation — the flow is strictly linear with no back-stack needs yet.
 * Revisit if Milestone 3 needs real back navigation (e.g. a session list with per-session detail).
 *
 * [UpdateRequiredState] is checked first and, once true, replaces this entire flow with
 * [UpdateRequiredScreen] regardless of [Destination] — matches iOS's `updateRequired` flag
 * showing `UpdateRequiredView` as a full-screen cover over whatever else was on screen.
 */
@Composable
fun DotWatcherApp(hasBackgroundLocationPermission: Boolean) {
    val isUpdateRequired by UpdateRequiredState.isUpdateRequired.collectAsState()
    if (isUpdateRequired) {
        val context = LocalContext.current
        DotWatcherTheme {
            UpdateRequiredScreen(onOpenPlayStore = {
                val intent = Intent(
                    Intent.ACTION_VIEW,
                    Uri.parse("https://play.google.com/store/apps/details?id=${context.packageName}"),
                )
                context.startActivity(intent)
            })
        }
        return
    }

    val viewModel: AppViewModel = viewModel()
    val destination by viewModel.destination.collectAsState()
    val authState by viewModel.authState.collectAsState()
    val sessionState by viewModel.sessionState.collectAsState()
    val recentSessions by viewModel.recentSessions.collectAsState()

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
                    recentSessions = recentSessions,
                    onCreateSession = viewModel::createSession,
                    onJoinSession = viewModel::joinSession,
                    onRejoinSession = viewModel::rejoinSession,
                    onRefresh = viewModel::loadRecentSessions,
                    onSignOut = viewModel::signOut,
                )

                is Destination.Consent -> if (hasBackgroundLocationPermission) {
                    ShareLocationConsentScreen(
                        sessionName = current.membership.sessionName,
                        onShare = { duration -> viewModel.startSharing(current.membership, duration) },
                        onDecline = { viewModel.declineSharing(current.membership) },
                    )
                } else {
                    // Background location was denied (or not yet granted) — sharing while
                    // backgrounded can't work, so skip straight to viewing rather than offer a
                    // consent screen for a feature that can't function.
                    MapDestinationScreen(current.membership, isSharing = false, viewModel)
                }

                is Destination.Map -> MapDestinationScreen(current.membership, current.isSharing, viewModel)
            }
        }
    }
}

@Composable
private fun MapDestinationScreen(membership: SessionMembership, isSharing: Boolean, viewModel: AppViewModel) {
    LiveMapScreen(
        membership = membership,
        repository = viewModel.repository,
        isSharing = isSharing,
        onStopSharing = { viewModel.stopSharing(membership) },
        onLeaveSession = { viewModel.leaveSession(membership) },
    )
}
