package nz.skelstar.dotwatcher.ui.map

import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.State
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LocalLifecycleOwner
import androidx.lifecycle.repeatOnLifecycle
import java.time.Instant
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive

/**
 * A 1 Hz "now" for countdown UI that only ticks while the app is visible (STARTED). A plain
 * `while (true) { delay(1_000) }` in a composable keeps waking the CPU every second with the
 * screen off, to update UI nobody can see; this stops entirely when backgrounded and refreshes
 * immediately on return.
 */
@Composable
fun rememberSecondClock(): State<Instant> {
    val now = remember { mutableStateOf(Instant.now()) }
    val lifecycle = LocalLifecycleOwner.current.lifecycle
    LaunchedEffect(lifecycle) {
        lifecycle.repeatOnLifecycle(Lifecycle.State.STARTED) {
            while (isActive) {
                now.value = Instant.now()
                delay(1_000L)
            }
        }
    }
    return now
}
