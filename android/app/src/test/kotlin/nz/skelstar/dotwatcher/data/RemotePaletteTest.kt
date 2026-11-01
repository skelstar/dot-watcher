package nz.skelstar.dotwatcher.data

import androidx.compose.ui.graphics.Color
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import nz.skelstar.dotwatcher.network.AuthResponse
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * The palette the server sends in `config.palette` (server/PaletteSettings.cs), as the app reads it: all of it
 * or none of it, so a bad value can never half-recolour the start screen. See .ai/plans/remote-palette.md.
 */
class RemotePaletteTest {
    private val json = Json { ignoreUnknownKeys = true }

    private fun palette(vararg overrides: Pair<String, String>): String {
        val values = linkedMapOf(
            "goBg" to "#6f1a07", "goFg" to "#f7f3e3", "goBar" to "#af9164",
            "altBg" to "#f7f3e3", "altFg" to "#2b2118",
            "accent" to "#af9164", "accentInk" to "#2b2118",
            "screen" to "#ffffff",
        )
        overrides.forEach { (k, v) -> values[k] = v }
        return """{"version":1,""" + values.entries.joinToString(",") { "\"${it.key}\":\"${it.value}\"" } + "}"
    }

    private fun parse(paletteJson: String) = RemotePalette.parse(json.parseToJsonElement(paletteJson))

    @Test fun `a good palette is read in full`() {
        val colours = parse(palette())!!
        assertEquals(Color(0x6f, 0x1a, 0x07), colours.goBg)
        assertEquals(Color(0xf7, 0xf3, 0xe3), colours.goFg)
        assertEquals(Color(0xaf, 0x91, 0x64), colours.goBar)
        assertEquals(Color(0xff, 0xff, 0xff), colours.screen)
    }

    @Test fun `a bad hex value rejects the whole palette`() {
        assertNull(parse(palette("goBg" to "6f1a07")))
        assertNull(parse(palette("screen" to "#fff")))
        assertNull(parse(palette("accent" to "#zzzzzz")))
    }

    @Test fun `a missing colour rejects the whole palette`() {
        assertNull(parse("""{"version":1,"goBg":"#6f1a07"}"""))
    }

    @Test fun `a version the app does not know is ignored`() {
        assertNull(parse(palette().replace("\"version\":1", "\"version\":2")))
        assertNull(parse(palette().replace("\"version\":1,", "")))
    }

    @Test fun `a text and background pair under 4_5 to 1 is rejected`() {
        assertNull(parse(palette("goFg" to "#7f2a17")))
        assertNull(parse(palette("altFg" to "#d0c8b0")))
        assertNull(parse(palette("accentInk" to "#a08060")))
    }

    @Test fun `contrast matches the WCAG reference values`() {
        assertEquals(21.0, RemotePalette.contrast("#000000", "#ffffff"), 0.01)
        assertEquals(1.0, RemotePalette.contrast("#336699", "#336699"), 0.01)
    }

    @Test fun `config without a palette, or with something else, means no palette`() {
        assertNull(RemotePalette.fromConfig(null))
        assertNull(RemotePalette.fromConfig(JsonObject(emptyMap())))
        assertNull(RemotePalette.fromConfig(json.parseToJsonElement("""{"palette":"red"}""")))
    }

    @Test fun `a sign-in response with a config reads the palette and one without still decodes`() {
        val withConfig = json.decodeFromString<AuthResponse>(
            """{"accessToken":"t","expiresAt":"x","user":{"userId":"u","username":"a","displayName":"AL",
               "features":["crew"],"config":{"palette":${palette()}}}}""",
        )
        assertNotNull(RemotePalette.fromConfig(withConfig.user.config))

        val without = json.decodeFromString<AuthResponse>(
            """{"accessToken":"t","expiresAt":"x","user":{"userId":"u","username":"a","displayName":"AL"}}""",
        )
        assertNull(without.user.config)
    }

    @Test fun `an unexpected config shape inside the user does not fail the sign-in`() {
        val auth = json.decodeFromString<AuthResponse>(
            """{"accessToken":"t","expiresAt":"x","user":{"userId":"u","username":"a","displayName":"AL",
               "config":{"palette":{"version":"one","goBg":42}}}}""",
        )
        assertNull(RemotePalette.fromConfig(auth.user.config))
    }
}
