package nz.skelstar.dotwatcher

import android.content.Intent
import android.net.Uri
import android.provider.Settings
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.Surface
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.viewmodel.compose.viewModel
import nz.skelstar.dotwatcher.network.SessionMembership
import nz.skelstar.dotwatcher.network.UpdateRequiredState
import nz.skelstar.dotwatcher.ui.crew.CrewConfirmDialog
import nz.skelstar.dotwatcher.ui.UpdateRequiredScreen
import nz.skelstar.dotwatcher.ui.auth.AuthScreen
import nz.skelstar.dotwatcher.ui.map.BackgroundLocationRequiredScreen
import nz.skelstar.dotwatcher.ui.map.CasualMapScreen
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
    val crewState by viewModel.crew.state.collectAsState()
    // Holding Go needs "all the time" location, as sharing in a session does.
    var casualNeedsLocationPermission by remember { mutableStateOf(false) }

    DotWatcherTheme {
        Surface(modifier = Modifier.fillMaxSize()) {
            when (val current = destination) {
                Destination.Auth -> AuthScreen(
                    state = authState,
                    onRegister = viewModel::register,
                    onLogin = viewModel::login,
                )

                Destination.Restoring -> Box(
                    modifier = Modifier.fillMaxSize(),
                    contentAlignment = Alignment.Center,
                ) {
                    CircularProgressIndicator()
                }

                Destination.Session -> if (casualNeedsLocationPermission && !hasBackgroundLocationPermission) {
                    val context = LocalContext.current
                    BackgroundLocationRequiredScreen(
                        sessionName = "Casual",
                        onOpenSettings = {
                            context.startActivity(
                                Intent(
                                    Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                                    Uri.fromParts("package", context.packageName, null),
                                )
                            )
                        },
                        onViewOnly = { casualNeedsLocationPermission = false },
                    )
                } else SessionScreen(
                    state = sessionState,
                    onCreateSession = viewModel::createSession,
                    onJoinSession = viewModel::joinSession,
                    onRefresh = viewModel::refresh,
                    onSignOut = viewModel::signOut,
                    account = viewModel.account,
                    onDeleteAccount = viewModel::deleteAccount,
                    crew = viewModel.crew,
                    casual = viewModel.casual,
                    onStartCasual = { duration ->
                        if (hasBackgroundLocationPermission) {
                            casualNeedsLocationPermission = false
                            viewModel.startCasual(duration)
                        } else {
                            casualNeedsLocationPermission = true
                        }
                    },
                )

                Destination.Casual -> CasualMapScreen(
                    repository = viewModel.repository,
                    casual = viewModel.casual,
                    crew = viewModel.crew,
                    onStop = viewModel::stopCasual,
                )

                is Destination.Consent -> if (hasBackgroundLocationPermission) {
                    ShareLocationConsentScreen(
                        sessionName = current.membership.sessionName,
                        onShare = { duration -> viewModel.startSharing(current.membership, duration) },
                        onDecline = { viewModel.declineSharing(current.membership) },
                    )
                } else {
                    // Background location isn't granted — sharing while backgrounded can't work,
                    // so explain why it's needed rather than offer a consent screen for a feature
                    // that can't function (or silently drop to view-only).
                    val context = LocalContext.current
                    BackgroundLocationRequiredScreen(
                        sessionName = current.membership.sessionName,
                        onOpenSettings = {
                            context.startActivity(
                                Intent(
                                    Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                                    Uri.fromParts("package", context.packageName, null),
                                )
                            )
                        },
                        onViewOnly = { viewModel.declineSharing(current.membership) },
                    )
                }

                is Destination.Map -> MapDestinationScreen(current.membership, current.isSharing, viewModel)
            }

            // "Add X to your crew?" can come from a crew link on any screen once signed in, so it
            // lives here rather than on the start screen. Nobody is added until they tap Add.
            crewState.confirm?.let { confirm ->
                if (destination != Destination.Auth) CrewConfirmDialog(confirm, viewModel.crew)
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
        onLeaveSession = { viewModel.leaveSession(membership) },
    )
}
