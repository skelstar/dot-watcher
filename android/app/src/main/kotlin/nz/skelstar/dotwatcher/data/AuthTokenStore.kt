package nz.skelstar.dotwatcher.data

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey

/**
 * Persists the signed-in user's access token, the rough Android equivalent of iOS's Keychain
 * usage for the same token (see repo root README.md, "Auth model"). Backed by
 * EncryptedSharedPreferences (AndroidX Security), which encrypts both keys and values at rest
 * using a key held in the Android Keystore.
 *
 * Uses the MasterKey/MasterKey.Builder API, which only exists from security-crypto 1.1.0-alpha01
 * onward (the last stable release, 1.0.0, only has the older, deprecated MasterKeys.getOrCreate
 * helper) — see gradle/libs.versions.toml's securityCrypto version.
 */
class AuthTokenStore(context: Context) {
    private val prefs: SharedPreferences = run {
        val masterKey = MasterKey.Builder(context)
            .setKeyScheme(MasterKey.KeyScheme.AES256_GCM)
            .build()

        EncryptedSharedPreferences.create(
            context,
            "dotwatcher_auth",
            masterKey,
            EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
            EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
        )
    }

    var accessToken: String?
        get() = prefs.getString(KEY_ACCESS_TOKEN, null)
        set(value) = prefs.edit().putString(KEY_ACCESS_TOKEN, value).apply()

    var userId: String?
        get() = prefs.getString(KEY_USER_ID, null)
        set(value) = prefs.edit().putString(KEY_USER_ID, value).apply()

    var displayName: String?
        get() = prefs.getString(KEY_DISPLAY_NAME, null)
        set(value) = prefs.edit().putString(KEY_DISPLAY_NAME, value).apply()

    val isSignedIn: Boolean
        get() = accessToken != null

    fun clear() {
        prefs.edit().clear().apply()
    }

    private companion object {
        const val KEY_ACCESS_TOKEN = "access_token"
        const val KEY_USER_ID = "user_id"
        const val KEY_DISPLAY_NAME = "display_name"
    }
}
