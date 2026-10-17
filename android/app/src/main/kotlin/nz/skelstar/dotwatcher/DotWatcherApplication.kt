package nz.skelstar.dotwatcher

import android.app.Application
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import nz.skelstar.dotwatcher.crash.CrashReporter
import nz.skelstar.dotwatcher.network.DotWatcherApiClient

/** Installs the crash reporter before anything else runs, and sends any crash saved by the
 *  previous run once the app has had a moment to start. */
class DotWatcherApplication : Application() {
    override fun onCreate() {
        super.onCreate()
        CrashReporter.install(this)

        CoroutineScope(SupervisorJob() + Dispatchers.IO).launch {
            // Not on the critical path of startup, and a crash loop shouldn't hammer the network.
            delay(3_000L)
            runCatching {
                CrashReporter.uploadPending(
                    this@DotWatcherApplication,
                    DotWatcherApiClient.create(BuildConfig.API_BASE_URL, this@DotWatcherApplication),
                )
            }
        }
    }
}
