package nz.skelstar.dotwatcher.network

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/**
 * Set once *any* request gets a `426 Upgrade Required` — the server rejecting this client's
 * `X-Api-Version` as below its `MinimumApiVersion` floor (repo root README.md, "Client
 * compatibility"). Matches iOS's `LocationManager.updateRequired` flag, set from the same
 * central low-level request function regardless of which endpoint triggered it
 * (LocationManager.swift's request-sending helper checks `statusCode == 426` once, not per call
 * site) — here that single check point is [UpdateRequiredInterceptor] rather than a shared
 * request function, since every call already goes through one shared OkHttpClient.
 *
 * A process-wide singleton rather than per-repository state because the app has exactly one
 * `DotWatcherApiClient` per process (see [DotWatcherApiClient.create]) and, like iOS, this should
 * block the *whole* app once true — there's no scenario where only part of the UI should keep
 * working against a server that has already declared this build unsupported.
 */
object UpdateRequiredState {
    private val _isUpdateRequired = MutableStateFlow(false)
    val isUpdateRequired: StateFlow<Boolean> = _isUpdateRequired.asStateFlow()

    fun markUpdateRequired() {
        _isUpdateRequired.value = true
    }
}
