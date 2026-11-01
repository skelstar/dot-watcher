package nz.skelstar.dotwatcher.data

import android.content.Context
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import nz.skelstar.dotwatcher.ui.theme.SignColors

/**
 * The last good start-screen palette from the server, kept so the next launch can use it before any
 * network call (see .ai/plans/remote-palette.md). Not sensitive, so plain SharedPreferences rather than
 * the encrypted token store.
 *
 * [applied] is read once, when the app starts, and does not change while it runs: a palette saved now
 * shows from the next launch, with no recolouring part-way through a session.
 */
class PaletteStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    /** The colours to use for this run of the app, or null for the built-in ones. */
    val applied: SignColors? = runCatching {
        prefs.getString(KEY_PALETTE, null)?.let { RemotePalette.parse(Json.parseToJsonElement(it)) }
    }.getOrNull()

    /**
     * Remembers the palette in a successful `config` response, or forgets it when the response has none
     * (the server's palette was removed or is invalid), so the app returns to the built-in colours. Call
     * only with a response that really arrived: a failed request must leave the saved palette alone.
     */
    fun save(config: JsonElement?) {
        val palette = (config as? JsonObject)?.get("palette")
        val editor = prefs.edit()
        if (RemotePalette.parse(palette) != null) {
            editor.putString(KEY_PALETTE, palette.toString())
        } else {
            editor.remove(KEY_PALETTE)
        }
        editor.apply()
    }

    private companion object {
        const val PREFS_NAME = "dotwatcher_palette"
        const val KEY_PALETTE = "palette"
    }
}
