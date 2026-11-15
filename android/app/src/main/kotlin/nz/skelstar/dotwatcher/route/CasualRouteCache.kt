package nz.skelstar.dotwatcher.route

import nz.skelstar.dotwatcher.network.CasualRouteRef

/**
 * The GPX routes the casual map draws: this person's own and those of crew who are out. GET
 * /casual/live lists only a reference per route ([CasualRouteRef]); the file itself is fetched from
 * `/casual/runs/{runId}/route`, and only again when its `uploadedAt` changes. Same rules as iOS's
 * CasualStore.syncRoutes. Not thread-safe: call [sync] from one coroutine at a time (the map's poll).
 */
class CasualRouteCache {
    private data class Loaded(val uploadedAt: String, val points: List<RoutePoint>)

    /** By run id. */
    private val loaded = mutableMapOf<String, Loaded>()

    /** The lines to draw now. */
    val lines: List<List<RoutePoint>> get() = loaded.values.map { it.points }

    /**
     * Fetches routes that are new or changed and forgets ones that are gone. A route that fails to
     * download, or has no track, is simply not drawn; the next poll tries again. [fetch] returns the
     * GPX bytes for a run id, or null on any failure.
     */
    suspend fun sync(refs: List<CasualRouteRef>, fetch: suspend (runId: String) -> ByteArray?) {
        val live = refs.map { it.runId }.toSet()
        loaded.keys.retainAll(live)
        for (ref in refs) {
            if (loaded[ref.runId]?.uploadedAt == ref.uploadedAt) continue
            val points = fetch(ref.runId)?.let(::parseGpxTrack) ?: continue
            if (points.isDrawable()) loaded[ref.runId] = Loaded(ref.uploadedAt, points)
        }
    }

    fun clear() = loaded.clear()
}
