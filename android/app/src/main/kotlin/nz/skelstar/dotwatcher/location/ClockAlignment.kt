package nz.skelstar.dotwatcher.location

import java.time.Instant

/**
 * Clock-aligned recording intervals (repo root README.md, "Position synchronisation"): instead
 * of "post every N seconds from whenever tracking started," snap to fixed wall-clock boundaries
 * (`:00`, `:15`, `:30`, `:45` for a 15s interval) so every runner's phone converges on the same
 * instants independently, without coordination — matching
 * ios/DotWatcher/DotWatcher/LocationManager.swift's `nextPostAt()`.
 */
fun nextPostAt(now: Instant, intervalSeconds: Long): Instant {
    val nowSeconds = now.epochSecond
    val nextEpochSecond = (Math.floorDiv(nowSeconds, intervalSeconds) + 1) * intervalSeconds
    return Instant.ofEpochSecond(nextEpochSecond)
}
