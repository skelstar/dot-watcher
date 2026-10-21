package nz.skelstar.dotwatcher.ui.map

import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.DashPathEffect
import android.graphics.Matrix
import android.graphics.Paint
import android.graphics.Path
import android.graphics.RectF
import androidx.core.graphics.PathParser
import nz.skelstar.dotwatcher.network.RunnerPosition
import java.time.Duration
import java.time.Instant
import java.time.OffsetDateTime

/** No position update for longer than this → [PinState.Stale], matching iOS's
 *  `NativeMapView.staleAfter` (ios/DotWatcher/DotWatcher/NativeMapView.swift). */
private val STALE_AFTER: Duration = Duration.ofSeconds(30)

/** No position update for longer than this → [PinState.Disconnected], matching iOS's
 *  `NativeMapView.disconnectedAfter`. */
private val DISCONNECTED_AFTER: Duration = Duration.ofSeconds(60)

/** Granularity of the own-dot send-countdown ring. One cached bitmap per step per color, so this
 *  stays small; 15 matches the 15 s post cadence (one step per second). */
const val RING_STEPS = 15

enum class PinState { Live, Stale, Disconnected }

/**
 * Everything that changes how one runner's marker is drawn. [key] doubles as the map style image
 * id, so equal specs share one bitmap and a changed spec simply selects (and lazily registers) a
 * different one.
 *
 * [ringStep] is only set for the local user while sharing: how many of [RING_STEPS] remain until
 * the next position send.
 */
data class PinSpec(
    val label: String,
    val colorIndex: Int,
    val state: PinState,
    val satellite: Boolean,
    val ringStep: Int?,
    /** Own dot only: automatic tracking is paused for a critical battery. Drawn grey with a low-battery
     *  badge and no countdown ring, since no send is coming. */
    val paused: Boolean = false,
) {
    val key: String get() = "pin|$label|$colorIndex|${state.ordinal}|$satellite|${ringStep ?: -1}|$paused"
}

/** The 1–2 letter tag drawn on a dot. Accounts are meant to carry 2-letter initials, but older
 *  accounts hold whatever was typed into a free-text field (e.g. an email handle), so reduce it the
 *  same way the web client's `initialsFor` and the server do: one word → its first two letters;
 *  several words → first and last word initials. Blank → "?". */
fun initialsFor(name: String): String {
    val words = name.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }
    return when {
        words.isEmpty() -> "?"
        words.size == 1 -> words[0].take(2).uppercase()
        else -> "${words.first()[0]}${words.last()[0]}".uppercase()
    }
}

/** Builds the marker spec for [position]. The local user's own pin is never stale or
 *  disconnected (it is this device's own data), mirroring iOS. */
fun pinSpecFor(
    position: RunnerPosition,
    currentUserName: String,
    now: Instant,
    ringStep: Int?,
    ownPaused: Boolean = false,
): PinSpec {
    val isMe = position.runnerName == currentUserName
    val age = if (isMe) null else runCatching {
        Duration.between(OffsetDateTime.parse(position.timestamp).toInstant(), now)
    }.getOrNull()
    val state = when {
        age == null -> PinState.Live
        age > DISCONNECTED_AFTER -> PinState.Disconnected
        age > STALE_AFTER -> PinState.Stale
        else -> PinState.Live
    }
    return PinSpec(
        label = initialsFor(position.runnerName),
        colorIndex = if (isMe) RunnerColorPalette.CURRENT_USER_INDEX else RunnerColorPalette.indexForName(position.runnerName),
        state = state,
        satellite = position.isUltraConstrained,
        ringStep = if (isMe && !ownPaused) ringStep else null,
        paused = isMe && ownPaused,
    )
}

private const val SATELLITE_PATH = "M4 4l5 5M15 15l5 5M9 9l6 6M13 5a6 6 0 0 1 6 6M13 1a10 10 0 0 1 10 10M3 21l4-4"

/** The runner's marker: an initials circle with a white border — which, on the own dot while
 *  sharing, doubles as the send-countdown ring — plus a satellite badge, and for stale/disconnected runners a faded or
 *  dashed style with a warning badge. All sizes in dp, scaled by [density]. */
fun pinBitmap(spec: PinSpec, density: Float): Bitmap {
    fun dp(v: Float) = v * density
    val size = dp(52f).toInt()
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val cx = size / 2f
    val cy = size / 2f
    val r = dp(17.5f)
    val color = if (spec.paused) 0xFF7D8794.toInt() else RunnerColorPalette.colorAt(spec.colorIndex).toInt()

    val textColor: Int
    when (spec.state) {
        PinState.Live -> {
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = color })
            val border = Paint(Paint.ANTI_ALIAS_FLAG).apply {
                style = Paint.Style.STROKE
                strokeWidth = dp(3f)
                this.color = 0xFFFFFFFF.toInt()
            }
            val step = spec.ringStep
            if (step == null) {
                canvas.drawCircle(cx, cy, r, border)
            } else {
                // Own dot while sharing: the white border *is* the send countdown, a full ring that
                // sweeps away toward the next post, over a faint track.
                border.strokeCap = Paint.Cap.ROUND
                border.alpha = 0x4D
                canvas.drawCircle(cx, cy, r, border)
                border.alpha = 0xFF
                val oval = RectF(cx - r, cy - r, cx + r, cy + r)
                canvas.drawArc(oval, -90f, 360f * step.coerceIn(0, RING_STEPS) / RING_STEPS, false, border)
            }
            textColor = 0xFFFFFFFF.toInt()
        }
        PinState.Stale -> {
            val fade = 140
            canvas.drawCircle(cx, cy, r, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = 0xFFFFFFFF.toInt(); alpha = fade })
            canvas.drawCircle(
                cx, cy, r,
                Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    style = Paint.Style.STROKE
                    strokeWidth = dp(2f)
                    this.color = color
                    alpha = fade
                },
            )
            textColor = (color and 0x00FFFFFF) or (fade shl 24)
        }
        PinState.Disconnected -> {
            canvas.drawCircle(
                cx, cy, r,
                Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    style = Paint.Style.STROKE
                    strokeWidth = dp(2f)
                    this.color = color
                    pathEffect = DashPathEffect(floatArrayOf(dp(4f), dp(4f)), 0f)
                },
            )
            textColor = color
        }
    }

    val text = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        this.color = textColor
        textSize = dp(12.5f)
        isFakeBoldText = true
        textAlign = Paint.Align.CENTER
    }
    canvas.drawText(spec.label, cx, cy - (text.descent() + text.ascent()) / 2, text)

    // Badges sit on the circle's upper-right edge; warning below satellite when both apply.
    val badgeR = dp(8.5f)
    val bx = cx + r * 0.78f
    var by = cy - r * 0.78f
    if (spec.paused) {
        // Red badge with a nearly-empty battery: says *why* the dot stopped updating. A little
        // larger than the other badges so the glyph stays legible.
        val pr = dp(10.5f)
        canvas.drawCircle(bx, by, pr, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = 0xFFFF3B30.toInt() })
        val bw = dp(11f)
        val bh = dp(6.5f)
        val body = RectF(bx - bw / 2 - dp(0.8f), by - bh / 2, bx + bw / 2 - dp(0.8f), by + bh / 2)
        val white = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.color = 0xFFFFFFFF.toInt()
            style = Paint.Style.STROKE
            strokeWidth = dp(1.2f)
        }
        canvas.drawRoundRect(body, dp(1.4f), dp(1.4f), white)
        white.style = Paint.Style.FILL
        // terminal nub, and a sliver of charge on the left
        canvas.drawRect(body.right + dp(0.6f), by - dp(1.3f), body.right + dp(1.8f), by + dp(1.3f), white)
        canvas.drawRect(body.left + dp(1.8f), body.top + dp(1.8f), body.left + dp(3.8f), body.bottom - dp(1.8f), white)
        by += pr + badgeR + dp(1f)
    }
    if (spec.satellite) {
        canvas.drawCircle(bx, by, badgeR, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = 0xFFFFCC00.toInt() })
        val glyph = PathParser.createPathFromPathData(SATELLITE_PATH)
        val scale = (badgeR * 1.2f) / 24f
        val m = Matrix().apply {
            setScale(scale, scale)
            postTranslate(bx - 12f * scale, by - 12f * scale)
        }
        Path(glyph).also { it.transform(m) }.let {
            canvas.drawPath(
                it,
                Paint(Paint.ANTI_ALIAS_FLAG).apply {
                    style = Paint.Style.STROKE
                    strokeWidth = dp(1.1f)
                    strokeCap = Paint.Cap.ROUND
                    strokeJoin = Paint.Join.ROUND
                    this.color = 0xFF14181F.toInt()
                },
            )
        }
        by += badgeR * 2 + dp(1f)
    }
    if (spec.state != PinState.Live) {
        canvas.drawCircle(bx, by, badgeR, Paint(Paint.ANTI_ALIAS_FLAG).apply { this.color = 0xFFFF3B30.toInt() })
        val bang = Paint(Paint.ANTI_ALIAS_FLAG).apply {
            this.color = 0xFFFFFFFF.toInt()
            textSize = dp(11f)
            isFakeBoldText = true
            textAlign = Paint.Align.CENTER
        }
        canvas.drawText("!", bx, by - (bang.descent() + bang.ascent()) / 2, bang)
    }
    return bitmap
}

/** Heading chevron that orbits the pin's edge: drawn near the top of a square canvas centred on
 *  the pin so rotating the whole icon by the heading swings it around the circle (the web
 *  client's `Arrow.tsx`, iOS's `ChevronShape`). */
fun chevronBitmap(colorArgb: Long, density: Float): Bitmap {
    fun dp(v: Float) = v * density
    val size = dp(60f).toInt()
    val bitmap = Bitmap.createBitmap(size, size, Bitmap.Config.ARGB_8888)
    val canvas = Canvas(bitmap)
    val cx = size / 2f
    // Tip sits just outside the own-dot countdown ring (pin r 17.5 + ring 4 + stroke), so the
    // chevron never overlaps it.
    val tipY = size / 2f - dp(25f)
    val path = Path().apply {
        moveTo(cx - dp(8f), tipY + dp(8f))
        lineTo(cx, tipY)
        lineTo(cx + dp(8f), tipY + dp(8f))
    }
    val stroke = Paint(Paint.ANTI_ALIAS_FLAG).apply {
        style = Paint.Style.STROKE
        strokeCap = Paint.Cap.ROUND
        strokeJoin = Paint.Join.ROUND
    }
    // White chevron outlined in the runner's colour so it stays visible on pale map areas.
    stroke.strokeWidth = dp(5.5f)
    stroke.color = colorArgb.toInt()
    canvas.drawPath(path, stroke)
    stroke.strokeWidth = dp(2.5f)
    stroke.color = 0xFFFFFFFF.toInt()
    canvas.drawPath(path, stroke)
    return bitmap
}
