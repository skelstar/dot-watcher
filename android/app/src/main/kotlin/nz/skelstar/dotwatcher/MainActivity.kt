package nz.skelstar.dotwatcher

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.network.DotWatcherApiClient
import nz.skelstar.dotwatcher.ui.theme.DotWatcherTheme

/**
 * Milestone 0 scaffold screen. Confirms the app can reach the server and decode a JSON
 * response end-to-end — see DotWatcherApi.getMySessions's kdoc for why a 401 here counts
 * as success. Replaced by the real sign-in/session flow in Milestone 1.
 */
sealed interface ConnectivityState {
    data object Loading : ConnectivityState
    data class Reached(val message: String) : ConnectivityState
    data class Failed(val message: String) : ConnectivityState
}

class MainViewModel : ViewModel() {
    private val api = DotWatcherApiClient.create(BuildConfig.API_BASE_URL)

    private val _state = MutableStateFlow<ConnectivityState>(ConnectivityState.Loading)
    val state: StateFlow<ConnectivityState> = _state

    init {
        checkConnectivity()
    }

    private fun checkConnectivity() {
        viewModelScope.launch {
            _state.value = ConnectivityState.Loading
            _state.value = try {
                val response = api.getMySessions()
                when (response.code()) {
                    401 -> ConnectivityState.Reached(
                        "Reached ${BuildConfig.API_BASE_URL} — server responded 401 " +
                            "(expected: no sign-in yet)."
                    )
                    else -> ConnectivityState.Reached(
                        "Reached ${BuildConfig.API_BASE_URL} — server responded ${response.code()}."
                    )
                }
            } catch (e: Exception) {
                ConnectivityState.Failed(
                    "Could not reach ${BuildConfig.API_BASE_URL}: ${e.message}"
                )
            }
        }
    }
}

class MainActivity : ComponentActivity() {
    private val viewModel: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            DotWatcherTheme {
                Surface(modifier = Modifier.fillMaxSize()) {
                    ConnectivityScreen(viewModel)
                }
            }
        }
    }
}

@Composable
fun ConnectivityScreen(viewModel: MainViewModel) {
    val state by viewModel.state.collectAsState()

    Scaffold { padding ->
        Column(
            modifier = Modifier
                .fillMaxSize()
                .padding(padding)
                .padding(24.dp),
            verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally,
        ) {
            Text(text = "Dot Watcher", style = MaterialTheme.typography.headlineMedium)
            Text(
                text = "Android scaffold — Milestone 0",
                style = MaterialTheme.typography.bodyMedium,
                modifier = Modifier.padding(top = 8.dp, bottom = 24.dp),
            )
            when (val current = state) {
                is ConnectivityState.Loading -> CircularProgressIndicator()
                is ConnectivityState.Reached -> Text(current.message)
                is ConnectivityState.Failed -> Text(current.message)
            }
        }
    }
}
