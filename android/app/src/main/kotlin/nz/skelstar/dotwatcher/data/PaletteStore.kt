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
 * Nothing here recolours the screen by itself: the app calls [current] at launch and each time it comes back
 * to the front, so a palette saved by a fetch shows from the next of those, never part-way through a view.
 */
class PaletteStore(context: Context) {
    private val prefs = context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)

    /** The saved palette's colours, or null for the built-in ones. */
    fun current(): SignColors? = runCatching {
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
