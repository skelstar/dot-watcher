package nz.skelstar.dotwatcher.data

import androidx.compose.ui.graphics.Color
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.intOrNull
import nz.skelstar.dotwatcher.ui.theme.SignColors
import kotlin.math.max
import kotlin.math.min
import kotlin.math.pow

/**
 * Reads the start-screen palette the server sends in `config.palette` (see .ai/plans/remote-palette.md).
 * The server has already checked it, but a bad palette must never break the screen, so the app checks
 * again and uses all of it or none of it: every colour is `#rrggbb`, the version is one we know, and the
 * three text-on-background pairs have at least [MIN_CONTRAST]:1 contrast. Anything else returns null and
 * the app keeps its built-in colours.
 */
object RemotePalette {
    const val VERSION = 1
    const val MIN_CONTRAST = 4.5

    private val hex = Regex("^#[0-9a-fA-F]{6}$")

    /** The palette inside a user's `config` object, or null if there is none or it isn't usable. */
    fun fromConfig(config: JsonElement?): SignColors? =
        parse((config as? JsonObject)?.get("palette"))

    fun parse(palette: JsonElement?): SignColors? {
        val obj = palette as? JsonObject ?: return null
        if ((obj["version"] as? JsonPrimitive)?.intOrNull != VERSION) return null

        fun colour(key: String): String? =
            (obj[key] as? JsonPrimitive)?.takeIf { it.isString }?.content?.takeIf { hex.matches(it) }

        val goBg = colour("goBg") ?: return null
        val goFg = colour("goFg") ?: return null
        val goBar = colour("goBar") ?: return null
        val altBg = colour("altBg") ?: return null
        val altFg = colour("altFg") ?: return null
        val accent = colour("accent") ?: return null
        val accentInk = colour("accentInk") ?: return null
        val screen = colour("screen") ?: return null

        if (contrast(goFg, goBg) < MIN_CONTRAST) return null
        if (contrast(altFg, altBg) < MIN_CONTRAST) return null
        if (contrast(accentInk, accent) < MIN_CONTRAST) return null

        return SignColors(
            goBg = toColor(goBg),
            goFg = toColor(goFg),
            goBar = toColor(goBar),
            altBg = toColor(altBg),
            altFg = toColor(altFg),
            accent = toColor(accent),
            accentInk = toColor(accentInk),
            screen = toColor(screen),
        )
    }

    /** WCAG contrast ratio between two `#rrggbb` colours. */
    fun contrast(a: String, b: String): Double {
        val la = luminance(a)
        val lb = luminance(b)
        return (max(la, lb) + 0.05) / (min(la, lb) + 0.05)
    }

    private fun channel(hex: String, start: Int): Int = hex.substring(start, start + 2).toInt(16)

    private fun toColor(hex: String) = Color(channel(hex, 1), channel(hex, 3), channel(hex, 5))

    private fun luminance(hex: String): Double {
        fun linear(start: Int): Double {
            val c = channel(hex, start) / 255.0
            return if (c <= 0.03928) c / 12.92 else ((c + 0.055) / 1.055).pow(2.4)
        }
        return 0.2126 * linear(1) + 0.7152 * linear(3) + 0.0722 * linear(5)
    }
}
