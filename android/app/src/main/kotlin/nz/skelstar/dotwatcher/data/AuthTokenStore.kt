package nz.skelstar.dotwatcher.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import java.security.KeyStore

/**
 * Persists the signed-in user's access token, the rough Android equivalent of iOS's Keychain
 * usage for the same token (see repo root README.md, "Auth model"). Backed by
 * EncryptedSharedPreferences (AndroidX Security), which encrypts both keys and values at rest
 * using a key held in the Android Keystore.
 *
 * Uses the MasterKey/MasterKey.Builder API, which only exists from security-crypto 1.1.0-alpha01
 * onward (the last stable release, 1.0.0, only has the older, deprecated MasterKeys.getOrCreate
 * helper) — see gradle/libs.versions.toml's securityCrypto version.
 *
 * **It must never crash the app.** The encrypted file is only readable with the Keystore key that
 * made it, and that key does not travel with the file: after a reinstall or a restore (Android's
 * auto-backup used to copy the file across), or if the key is invalidated, decrypting throws
 * `AEADBadTagException`. That used to happen while the first screen's view model was being
 * created, crashing the app on every launch. Now an unreadable store is reset (the file and the
 * old key are deleted, so the user simply signs in again), and if even that fails the values are
 * held in memory for this run only.
 */
class AuthTokenStore(context: Context) {
    private val appContext = context.applicationContext

    private var store: Store = openStore()

    var accessToken: String?
        get() = read(KEY_ACCESS_TOKEN)
        set(value) = write(KEY_ACCESS_TOKEN, value)

    var userId: String?
        get() = read(KEY_USER_ID)
        set(value) = write(KEY_USER_ID, value)

    var displayName: String?
        get() = read(KEY_DISPLAY_NAME)
        set(value) = write(KEY_DISPLAY_NAME, value)

    /** Null for a sign-in made before the username was stored; refreshed on the next login. */
    var username: String?
        get() = read(KEY_USERNAME)
        set(value) = write(KEY_USERNAME, value)

    val isSignedIn: Boolean
        get() = accessToken != null

    fun clear() {
        guarded(Unit) { store.clear() }
    }

    private fun read(key: String): String? = guarded(null) { store.get(key) }

    private fun write(key: String, value: String?) {
        guarded(Unit) { store.set(key, value) }
    }

    /** Runs [block]; if the store turns out to be unusable, resets it and returns [fallback] (the
     *  caller then behaves as signed out) rather than letting the exception reach the UI. */
    private fun <T> guarded(fallback: T, block: () -> T): T = try {
        block()
    } catch (e: Exception) {
        store = openStore(forceReset = true)
        fallback
    }

    private fun openStore(forceReset: Boolean = false): Store {
        if (forceReset) reset()
        return try {
            PrefsStore(openEncryptedPrefs())
        } catch (e: Exception) {
            // Unreadable with this device's key: throw it away and start again.
            reset()
            try {
                PrefsStore(openEncryptedPrefs())
            } catch (e2: Exception) {
                MemoryStore()
            }
        }
    }

    private fun openEncryptedPrefs(): SharedPreferences {
        val masterKey = MasterKey.Builder(appContext)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        return EncryptedSharedPreferences.create(
            appContext,
            PREFS_NAME,
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    /** Deletes the unreadable file (the Tink keysets live inside it) and the Keystore master key,
     *  so the next open generates both afresh. */
    private fun reset() {
        runCatching { appContext.deleteSharedPreferences(PREFS_NAME) }
        runCatching {
            KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
                .deleteEntry(MasterKey.DEFAULT_MASTER_KEY_ALIAS)
        }
    }

    private interface Store {
        fun get(key: String): String?
        fun set(key: String, value: String?)
        fun clear()
    }

    private class PrefsStore(private val prefs: SharedPreferences) : Store {
        override fun get(key: String): String? = prefs.getString(key, null)
        override fun set(key: String, value: String?) = prefs.edit().putString(key, value).apply()
        override fun clear() = prefs.edit().clear().apply()
    }

    /** Last resort: nothing is persisted, so the user signs in again next launch. */
    private class MemoryStore : Store {
        private val values = mutableMapOf<String, String?>()
        override fun get(key: String): String? = values[key]
        override fun set(key: String, value: String?) { values[key] = value }
        override fun clear() = values.clear()
    }

    private companion object {
        const val PREFS_NAME = "dotwatcher_auth"
        const val KEY_ACCESS_TOKEN = "access_token"
        const val KEY_USER_ID = "user_id"
        const val KEY_DISPLAY_NAME = "display_name"
        const val KEY_USERNAME = "username"
    }
}
