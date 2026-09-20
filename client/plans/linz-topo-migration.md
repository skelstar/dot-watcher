# Plan: LINZ topographic map tiles in the web client

> **Status:** in progress — Phases 0–4, 6 and 7 (basemap style toggle) code/doc work done, real map tiles confirmed rendering locally. LINZ API key obtained and in use in `client/.env` (2026-09-21) — still needs copying into the Tatooine deploy `.env` at deploy time (see task 2.4). Phase 5 (manual on-device check, now including the toggle) and task 4.3 (open the PR) are next.
> **Suggested location in repo:** `client/docs/linz-topo-migration.md`
> **Resume on any machine:** open Claude Code in the repo root and paste:
> `Read client/docs/linz-topo-migration.md and continue from the first unchecked task. Follow the Rules for the agent section.`

---

## Goal

Replace the client's Mapbox basemap with the LINZ Basemaps topographic vector tiles so runners and watchers see a proper NZ topo map (contours, tracks, relief shading) instead of a generic street map.

Because LINZ recommends MapLibre for vector tiles and Mapbox GL JS still needs a Mapbox token and billing even with third-party styles, this means swapping `mapbox-gl` for `maplibre-gl`. The API is nearly identical, so runner markers, heading arrows and `fitBounds` should carry over with little or no change.

Only the map library and basemap style(s) change (Phase 7 adds a second LINZ style with a toggle between them, at the human's request — still just a style/library change, not new map data or a non-LINZ fallback). The server, iOS app, polling, auth and marker logic are out of scope.

---

## Rules for the agent (Claude Code)

- **Do not run build, publish, test or dev-server commands.** `AGENTS.md` says GitHub Actions CI owns verification. Re-read `AGENTS.md` first and follow it if it differs from this summary.
- Adding or removing dependencies so that `package.json` and `package-lock.json` stay consistent is expected. Do that with npm, and do nothing else with the toolchain.
- **Never commit a real API key.** Use `VITE_LINZ_API_KEY` from an env file that is gitignored (`.env.local`) and document it in `.env.example`.
- Work through the task list in order. After finishing a task, tick its checkbox and add a dated line to **Progress log** at the bottom of this file. Commit the doc update with the code change.
- Do not guess file names. Task 0.2 records where the real files are. Update this doc with what you find.
- If something in this plan turns out to be wrong (for example the style name, or an option that doesn't exist in the installed MapLibre version), fix the plan and note it in the Progress log instead of working around it silently.
- Stop and ask the human where a task says **[HUMAN]**.

---

## Facts this plan relies on (verified against LINZ docs, Sept 2026)

- Style JSON: `https://basemaps.linz.govt.nz/v1/styles/{tileset_name}.json?api=API_KEY`. The docs mention both `topographic` and `topographic-v2`, so **confirm the current name** (task 0.3).
- Tiles: `https://basemaps.linz.govt.nz/v1/tiles/{tileset}/{crs}/{z}/{x}/{y}.pbf?api=API_KEY`. Use `3857` (Web Mercator) for web apps.
- Access: without registration there is a limited "Standard" tier (1,000 tiles/min, 1,000,000 tiles/month). A **free Developer API key** gives unlimited reasonable use. For a shared or public app, request a Developer key rather than an individual one.
  - **How to test before the Developer key arrives:** visit https://basemaps.linz.govt.nz in a browser and grab a map/tile API URL from the site's menu bar — this auto-issues a "Standard" tier key with no registration/waiting (rate-limited as above, expires after 90 days). Put that value straight into `client/.env` as `VITE_LINZ_API_KEY=...`; no code change needed to swap it for the real Developer key later, since `mapStyle.ts` only reads the env var.
- Licence: CC BY 4.0. The product must visibly show attribution and link to the LINZ copyright statement and the Basemap's custom attribution text. The mapping library does not do this automatically.
- Styling: tiles use the Shortbread schema. LINZ defines land polygons rather than ocean polygons, so in a custom style the background is water and land is drawn on top.
- Coverage is New Zealand only. Outside NZ the map will be blank or empty.
- An older tile.json listed a max zoom of 15. MapLibre overzooms past that, but check how it looks when zoomed in on a trail.
- **`maplibre-gl@6` requires an explicit `setWorkerUrl()` call before creating any `Map`, or every vector/GeoJSON source hangs forever with no error.** v5 auto-inlined the worker; v6 only auto-detects the worker URL for plain CDN `<script type="module">` loading — under any bundler (Vite included), `import.meta.url`-based auto-detection doesn't resolve correctly, so the worker never starts. Symptoms exactly match ours: `style.json`/`tile.json`/sprite/glyphs all fetch fine (main thread), `map.isStyleLoaded()` and `map.isSourceLoaded(id)` stay `false` forever, **zero `.pbf` tile requests ever fire**, no console error, no exception — it just silently hangs. Raster/raster-dem sources are unaffected (no worker needed), which is why LINZ's hillshade/terrain tiles loaded fine while every vector source (LINZ's `topographic-v2`/`aerialhybrid` vector overlay, and our own local `gpx-route`/`simulator-routes` GeoJSON sources) didn't. Confirmed as a known, documented v5→v6 migration requirement ([migration guide](https://maplibre.org/maplibre-gl-js/docs/guides/v5-to-v6-migration-guide/), [maplibre-gl-js#8018](https://github.com/maplibre/maplibre-gl-js/issues/8018), [#8459](https://github.com/maplibre/maplibre-gl-js/issues/8459)) — not something specific to this repo or to LINZ's tiles. Vite fix: `import workerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'` (must be `?worker&url`, not plain `?url` — the raw dist worker file imports a sibling `maplibre-gl-shared.mjs` that only the `?worker&url` pipeline brings along) then `setWorkerUrl(workerUrl)` once at module scope before any `Map` is constructed. If you're on a different `maplibre-gl` major version or bundler, re-check whether this still applies.
- **`maplibre-gl@6.10.0` has no default export** — `import maplibregl from 'maplibre-gl'` (this plan's original assumption, matching `mapbox-gl`) fails at runtime with `Uncaught SyntaxError: ... does not provide an export named 'default'`. Use named imports instead: `import { MapLibreMap, Marker, NavigationControl, GeolocateControl, LngLat, LngLatBounds, type GeoJSONSource, type AnimationOptions } from 'maplibre-gl'`. TypeScript's `esModuleInterop` hides this at compile time (a default import type-checks fine against a namespace-shaped module); it only shows up once the browser loads the real ESM bundle. If you're on a different `maplibre-gl` major version, re-check whether this still applies before assuming it does.

---

## Open decisions

- [ ] **[HUMAN]** Outside NZ: accept a blank map (NZ-only), or keep a Mapbox/OSM fallback with a style toggle? *Default if not answered: NZ-only, revisit later.* **Still on the default (NZ-only)** — the style toggle added in Phase 7 switches between two LINZ styles (both NZ-only), not to a non-LINZ fallback, so it doesn't answer this one.
- [x] Where is the client built and deployed (GitHub Actions and Tatooine), so the build knows where to get `VITE_LINZ_API_KEY`? — **Answered from `client/README.md`, no human input needed:** GitHub Actions CI (build+test verification, placeholder key only) and Tatooine/k3s via the `/deploy` skill (real key goes in a gitignored `.env` copied to the host before deploy). See task 2.4.

---

## Task list

### Phase 0: Prep
- [x] 0.1 **[HUMAN]** Request a free Developer API key at https://basemaps.linz.govt.nz (do not paste it into chat or commit it).
  - 2026-09-21: request submitted, key obtained, and already in use in `client/.env` (confirmed working — real tiles render, see the `setWorkerUrl` debugging session). Still needs copying into the Tatooine deploy `.env` at `/home/skelstar/deployments/dot-watcher-client/src/.env` when the human next deploys — see task 2.4's existing instructions; that's a deploy-time step, not something to do from here.
- [x] 0.2 Locate and record here: the map component(s), where the map is created, where Mapbox is imported, where the token is configured, how markers are built, and any tests that mock `mapbox-gl`. Note whether the client uses `react-map-gl`.
  - No `react-map-gl` — the client uses `mapbox-gl` directly. Only `client/package.json` depends on it (the `tools/simulator` package is unrelated: it uses `leaflet`/`react-leaflet`, and its one "Mapbox" comment just documents `[lon, lat]` coordinate order, not the library).
  - Map created in [client/src/App.tsx](client/src/App.tsx#L108): `new mapboxgl.Map({ container, style: 'mapbox://styles/mapbox/streets-v12', center, zoom })`, plus `NavigationControl` and `GeolocateControl` added right after.
  - Token configured at [client/src/App.tsx:27](client/src/App.tsx#L27): `mapboxgl.accessToken = import.meta.env.VITE_MAPBOX_TOKEN`. Also referenced in `client/README.md`, root `README.md`, and `.github/workflows/client-node.yml:38` (`VITE_MAPBOX_TOKEN: ci-mapbox-token-placeholder` for the CI build step).
  - Markers built in [client/src/useRunnerMarkers.ts](client/src/useRunnerMarkers.ts#L139): `new mapboxgl.Marker({ element: el, offset: [0, 0] })`. **Heading rotation is not done via the Marker's own rotation option** — the heading is passed into a React `Dot`/`Arrow` component rendered inside the marker's DOM element (CSS transform), so the code sketch in this plan about `new maplibregl.Marker({ element, rotation })` doesn't apply here; nothing needs to change beyond the constructor still accepting `{ element, offset }`, which MapLibre supports identically.
  - Also imports `mapboxgl` for `LngLat`/`LngLatBounds` and `fitBounds`/`easeTo` in `useRunnerMarkers.ts` and [client/src/useRouteLayer.ts](client/src/useRouteLayer.ts) (route line/arrow/start-finish layers via `addSource`/`addLayer`/`addImage`) and [client/src/useSimulatorRouteOverlay.ts](client/src/useSimulatorRouteOverlay.ts) (GeoJSON source updates). All of these use APIs MapLibre GL JS supports with the same shape (it's a fork of Mapbox GL JS v1).
  - `client/src/Legend.tsx` and `client/src/LegendHelp.tsx` only mention "Mapbox" in comments describing control positioning/behaviour (`NavigationControl` placement) — no imports, cosmetic only.
  - No test currently mocks `mapbox-gl` (`src/*.test.ts` covers `sessionLiveness`, `sessionState`, `useSessionTimelineLogic` only) — task 4.1 is likely a no-op unless new tests are added.
- [x] 0.3 Confirm the current tileset name (`topographic` vs `topographic-v2`) from https://basemaps.linz.govt.nz/docs and record it under **Facts**.
  - Confirmed via the current LINZ docs ([Working with vector tiles: usage](https://basemaps.linz.govt.nz/docs/user-guide/working-with-vector-tiles/usage/)): the canonical, actively-documented tileset is **`topographic-v2`**, using the style URL pattern already assumed in this plan (`/v1/styles/{tileset}.json?api=`). `topographic` (no `-v2`) still exists in the [`linz/basemaps-config`](https://github.com/linz/basemaps-config/tree/master/config/style) repo and one older examples page still uses it, but the current usage guide's primary example is `topographic-v2`, last updated Sept 2025 (vs July 2025 for the plain style) — using `topographic-v2` as planned.

### Phase 1: Swap the library
- [x] 1.1 Add `maplibre-gl`. If `react-map-gl` is used, keep it and import from `react-map-gl/maplibre`.
  - No `react-map-gl` in this project (confirmed in 0.2), so a plain `npm install maplibre-gl` in `client/`. Installed `maplibre-gl@^6.10.0`.
  - **Added 2026-09-21, found only through live debugging (not caught by any of the checks below):** v6 also requires a one-time `setWorkerUrl()` call before creating a `Map`, or every vector/GeoJSON source hangs forever with zero tile requests and no error — see the new Facts entry above for the full story and the fix, now in `App.tsx`. This is arguably part of 1.1 (installing/wiring up the library correctly), not 1.2/1.4 — recorded here since that's where it was found to belong in hindsight.
- [x] 1.2 Replace `mapbox-gl` imports with `maplibre-gl`, and the CSS import with `maplibre-gl/dist/maplibre-gl.css`.
  - Updated `App.tsx`, `useRunnerMarkers.ts`, `useRouteLayer.ts`, `useSimulatorRouteOverlay.ts` (import + every `mapboxgl.` → `maplibregl.` reference, including types like `maplibregl.Map`/`maplibregl.Marker`/`maplibregl.GeoJSONSource`). Also reworded stray "Mapbox" comments in `Legend.tsx`, `LegendHelp.tsx`, `useRunnerMarkers.ts`, `useRouteLayer.ts` for accuracy (none needed logic changes).
  - **Corrected 2026-09-21:** the first pass used a default import (`import maplibregl from 'maplibre-gl'`), mirroring `mapbox-gl`'s API. That's wrong for `maplibre-gl@6.10.0` — see the "Corrected" note on the Map-creation code sketch below and the Progress log for the full story (TypeScript didn't catch it; it only broke in the browser). Redone as named imports (`import { MapLibreMap, Marker, NavigationControl, GeolocateControl, LngLat, LngLatBounds, type GeoJSONSource, type AnimationOptions } from 'maplibre-gl'`) across all four files.
- [x] 1.3 Remove `mapboxgl.accessToken` and any Mapbox token env var usage.
  - Removed the `mapboxgl.accessToken = import.meta.env.VITE_MAPBOX_TOKEN` line from `App.tsx` (MapLibre needs no access token). `VITE_MAPBOX_TOKEN` usage is fully gone from `client/src`.
- [x] 1.4 Confirm markers, heading rotation and `fitBounds` use only APIs that exist in MapLibre. Adjust `new mapboxgl.Marker(el)` to `new maplibregl.Marker({ element: el, rotation })` if needed.
  - Confirmed in 0.2: heading rotation is done inside the React `Arrow`/`Dot` component (CSS transform), not via the Marker's own `rotation` option, so the plan's rotation sketch doesn't apply — `new maplibregl.Marker({ element: el, offset: [0, 0] })` (unchanged shape) is correct as-is. Verified `Marker`, `NavigationControl`, `GeolocateControl`, `LngLat`, `LngLatBounds`, `GeoJSONSource`, `addSource`/`addLayer`/`addImage`/`fitBounds`/`easeTo` all exist in the installed `maplibre-gl` typings with matching option shapes.
- [x] 1.5 Remove `mapbox-gl` from dependencies once nothing imports it.
  - `npm uninstall mapbox-gl` in `client/`. Confirmed zero remaining references to `mapbox-gl` or `mapboxgl` anywhere under `client/` (source or lockfile) outside this plan doc's own history notes.

### Phase 2: LINZ style
- [x] 2.1 Create a small module (for example `src/map/mapStyle.ts`) exporting the style URL built from `import.meta.env.VITE_LINZ_API_KEY`.
  - Added `client/src/map/mapStyle.ts` exporting `LINZ_TOPO_STYLE` (built from `topographic-v2`, per 0.3) and `LINZ_ATTRIBUTION` (see Phase 3).
- [x] 2.2 Point the map's `style` option at it.
  - `App.tsx`'s `new maplibregl.Map({...})` now uses `style: LINZ_TOPO_STYLE`.
- [x] 2.3 Add `VITE_LINZ_API_KEY=` to `.env.example`. Ensure `.env.local` is gitignored.
  - **Deviation from the plan, noted per the "Rules for the agent":** this project's existing convention (see `client/README.md`) is a plain gitignored `client/.env`, not `.env.local` — `.env` is already covered by the root `.gitignore` (`.env` on line 26) exactly like `.env.local` (line 27) is, and the Tatooine deploy flow (below) already depends on copying a `.env` file. Introducing a second, differently-named env file would fork that convention for no benefit, so `VITE_LINZ_API_KEY` was added to a new `client/.env.example` (there wasn't one before) instead, following the existing `.env` name. Verified `client/.env` (real, untouched) and `client/.env.local` are both gitignored.
- [x] 2.4 Make the key available at build time in CI and the deploy pipeline. **[HUMAN]** adds the secret; the agent updates workflow or Dockerfile references and notes what was changed.
  - CI (`.github/workflows/client-node.yml`): renamed the build-step env var from `VITE_MAPBOX_TOKEN: ci-mapbox-token-placeholder` to `VITE_LINZ_API_KEY: ci-linz-api-key-placeholder` — CI only needs a placeholder to exercise the Vite build, same as before.
  - Deploy (Tatooine, documented in `client/README.md`): this is **not a GitHub secret** — per the existing (unchanged) deploy flow, the real value goes into a gitignored `.env` file copied to `/home/skelstar/deployments/dot-watcher-client/src/.env` on the Tatooine host before `/deploy update dot-watcher-client` runs. Updated the README's example `.env` block from `VITE_MAPBOX_TOKEN=your-mapbox-token` to `VITE_LINZ_API_KEY=your-linz-api-key`. **[HUMAN]**: once a Developer key exists (0.1), put it in your local `client/.env` and in that Tatooine `src/.env` file — no code/workflow change needed for that step, just the value.
  - This also answers the second **Open decision** above (where the client is built/deployed): GitHub Actions CI (build+test verification only, placeholder key) and Tatooine/k3s (real deploy, `.env` copied to the host) — already documented in `client/README.md`, so no human input was needed to answer it.

### Phase 3: Attribution
- [x] 3.1 Check whether the LINZ style already supplies attribution text on its sources.
  - Checked `config/style/topographic-v2.json` in `linz/basemaps-config`: no `attribution` field anywhere (top-level or per-source) — confirmed we must supply our own.
- [x] 3.2 Add visible attribution linking to the LINZ copyright statement (https://www.linz.govt.nz/linz-copyright) and to the Basemaps data-attribution page. Check the exact MapLibre option shape (`customAttribution`, or an explicit `AttributionControl`) against the installed version.
  - Installed `maplibre-gl` typings confirm `Map`'s `attributionControl` constructor option accepts `false | { compact?: boolean; customAttribution?: string | string[] }` — matches this plan's sketch exactly. `App.tsx` now passes `attributionControl: { customAttribution: LINZ_ATTRIBUTION }`, where `LINZ_ATTRIBUTION` (in `mapStyle.ts`) links to `https://www.linz.govt.nz/linz-copyright` and to `https://www.linz.govt.nz/data/linz-data/linz-basemaps/data-attribution` (verified this is the correct current LINZ Basemaps data-attribution page, not the one guessed initially — see git history of this file if curious).
- [x] 3.3 Make sure no UI element (bottom sheet, buttons) covers the attribution on mobile.
  - Reasoned through this from the code (couldn't visually verify — no dev server per `AGENTS.md`): MapLibre's default `AttributionControl` sits bottom-right and auto-collapses to a small "i" icon on maps under 640px wide (unforced default, left as-is — forcing `compact: true` isn't recommended per MapLibre's own docs unless needed). The app's own bottom-anchored UI (`versionBadge` bottom-left, `ReplayControls` bar mostly `pointerEvents: 'none'` and centred, status/toast elements horizontally centred) is all bottom-left or bottom-centre, not bottom-right, so no structural overlap is expected. **Flagging this for a real on-device check in Phase 5 (5.6 already covers "attribution visible")** rather than treating it as fully verified.

### Phase 4: Tests and CI
- [x] 4.1 Update tests and mocks that reference `mapbox-gl`.
  - Confirmed in 0.2 and re-checked: no test file mocks or imports `mapbox-gl`/`maplibre-gl`. No-op.
- [x] 4.2 Confirm the client CI workflow needs no changes beyond the env var (`npm ci`, `npm run test`, `npm run build`).
  - Confirmed — only the env var name/value changed (2.4); `npm ci` / `npm run test` / `npm run build` steps are untouched.
  - **One residual risk worth CI's specific attention:** the `?worker&url` worker-URL import added for `setWorkerUrl` (see Facts, 1.1) is verified working under `npm run dev` (Vite dev server), but its production-build behaviour was **not** separately verified locally — running `npm run build` is CI's job per `AGENTS.md`, not something done here. This is flagged specifically because the official migration guide explicitly warns that the *wrong* variant of this import (`?url` instead of `?worker&url`) "emits the worker file verbatim in production builds" and silently breaks only in prod, not dev — i.e. this exact area is a known place where dev and prod can diverge. If CI's build succeeds, this is a non-issue; if the deployed Tatooine build ever shows the same silent blank-map symptom despite `npm run dev` working, re-check this first.
- [ ] 4.3 Open the PR. CI is the verification; do not run the toolchain locally.
  - Asked the human whether to push/open now vs. wait for 0.1 + Phase 5 first — chose to hold off. Commit `395a3bc` is local to `client-linz-maps` only; nothing pushed.

### Phase 5: Manual check **[HUMAN]**
- [ ] 5.1 Runners' dots and heading arrows render and update on the LINZ map.
- [ ] 5.2 Auto-fit to all runners still works.
- [ ] 5.3 Contours, tracks and labels are readable on a phone in daylight. **Superseded, see Phase 7:** rather than restyling `topographic-v2` in Maputnik, urban readability is now handled by the Phase 7 style toggle (switch to the `aerial` LINZ style, which shows real imagery/streets) instead of a local custom style — check whether `topographic-v2` alone is still acceptable for trail/off-road sections, and whether the toggle is an adequate answer for urban ones.
- [ ] 5.4 Zoomed right in past z15, tiles still look acceptable.
- [ ] 5.5 Network tab shows no 401/403 tile errors. **Note:** this was where the `setWorkerUrl` bug (see Facts) actually surfaced during dev debugging on 2026-09-21 — not as a 401/403, but as *zero* `.pbf` requests at all, map stuck showing only the flat background colour. Confirmed fixed locally (real tiles now load on both `topo` and `aerial`); re-check here as normal but this specific failure mode is already understood if it recurs.
- [ ] 5.6 Attribution is visible and both links work.
- [ ] 5.7 Toggle to the `aerial` style and back (Phase 7): runner markers, the route line/arrows/start-finish icons, and attribution all survive the switch each way, and the toggle button (top-right, below Geolocate) doesn't overlap the other top-right controls at phone width.

### Phase 6: Docs
- [x] 6.1 Update the root `README.md` and any client README that say "Mapbox GL JS" or "Mapbox" (the Components > client section, the structure summary).
  - Updated `README.md` (repo structure line, "how it works" step 5, Components > client intro + first bullet) and the generated-looking `README.html` (same three spots — it had already drifted slightly from `README.md` before this change, unrelated to this plan). `client/README.md` covered under 2.4/2.3 above. Remaining "Mapbox" mention is a deliberate historical comparison in the new env-var table row, not a leftover.
- [x] 6.2 Document `VITE_LINZ_API_KEY`, where to get one, and the NZ-only coverage note.
  - `client/README.md`: Prerequisites section names the free LINZ Basemaps Developer key and where to request it; the env var table's `VITE_LINZ_API_KEY` row states the NZ-only coverage note.

### Phase 7: Basemap style toggle (added 2026-09-21, human request — topo reads poorly on urban routes)

LINZ Basemaps has no generic "streets" style (it's a topo-and-imagery product, not street cartography). The two genuinely different options in its catalogue: `topographic-v2` (current default — vector, contour/trail-oriented, roads drawn simply with no buildings) and `aerialhybrid` (real LINZ aerial imagery + a road/label overlay — shows actual streets and buildings, reads much better in town). Decision: add a two-way toggle between these rather than picking one, and rather than a non-LINZ fallback (keeps the "outside NZ" open decision above unanswered/unaffected — both styles are still NZ-only).

- [x] 7.1 Add a style registry (`MAP_STYLES` in `src/map/mapStyle.ts`) with `topo` and `aerial` entries, each a full style URL built from `VITE_LINZ_API_KEY` (same `topographic-v2` URL as before, plus a new `aerialhybrid` one), and a `DEFAULT_MAP_STYLE_ID` of `topo`.
- [x] 7.2 Add a `MapStyleToggle` button (`src/MapStyleToggle.tsx`), styled to match `LegendHelp`'s "?" button and stacked directly below it (top-right column, under Nav/Geolocate). Shows a layers icon; tooltip/label always names the style you'd switch *to*, not the current one.
- [x] 7.3 Wire it up in `App.tsx`: `mapStyleId` state for the button's label, plus an imperative `mapRef.current?.setStyle(...)` on toggle — deliberately **not** a dependency of the map-creation effect, so toggling swaps the live map's style instead of tearing down and recreating the whole `Map` instance (which would lose camera position/following state).
- [x] 7.4 Make the route line/arrows/start-finish icons and the simulator route overlay survive a style swap: `useRouteLayer.ts` and `useSimulatorRouteOverlay.ts` previously used `map.once('load', ...)`, which only ever fires once for the map's life. Switched both to `map.on('style.load', ...)`, which MapLibre fires for the initial style **and every later `setStyle()` call** — exactly what's needed to re-add the wiped sources/layers/images after a toggle. (Runner markers needed no change: they're plain DOM `Marker` overlays, untouched by style changes.)
- [x] 7.5 Attribution: confirmed `aerialhybrid`'s own "LINZ Basemaps" label source carries a source-level `attribution` field (`"© 2022 Toitū Te Whenua - CC BY 4.0"`), which MapLibre's `AttributionControl` will show *in addition to* the `customAttribution` already set (Phase 3) when that style is active — expect two similar-looking LINZ notices together while on `aerial`. Left as-is rather than trying to suppress LINZ's own attribution text.
- [ ] 7.6 **[HUMAN]** On-device check — see task 5.7.

---

## Code sketches (starting points, adapt to the real code)

**Style module**
```ts
// src/map/mapStyle.ts
const key = import.meta.env.VITE_LINZ_API_KEY;

export const LINZ_TOPO_STYLE =
  `https://basemaps.linz.govt.nz/v1/styles/topographic-v2.json?api=${key}`;
```

**Map creation**

> **Corrected 2026-09-21 — see Progress log.** The sketch below (and the version actually
> committed first) used `import maplibregl from 'maplibre-gl'` — a *default* import, matching
> `mapbox-gl`'s API. **`maplibre-gl@6.10.0` (the version this plan installed) has no default
> export at all** — everything (`Map`, `Marker`, `NavigationControl`, `GeolocateControl`,
> `LngLat`, `LngLatBounds`, `GeoJSONSource`, ...) is a named export only. A default import throws
> `Uncaught SyntaxError: The requested module ... does not provide an export named 'default'` at
> runtime — TypeScript doesn't catch this at compile time, since `esModuleInterop` lets a default
> import silently mean "the whole namespace" for a CJS-shaped module; this only surfaces once the
> browser actually loads the real ESM module. Named imports (below) are what's actually in the
> repo now.

> **Also added 2026-09-21:** the worker URL setup from the new Facts entry above (`setWorkerUrl`
> call), without which the sketch below renders only a flat background colour and never fetches
> a single vector tile.

```ts
import { MapLibreMap, NavigationControl, GeolocateControl, setWorkerUrl } from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'; // Vite-specific
import { MAP_STYLES, DEFAULT_MAP_STYLE_ID } from './mapStyle';

setWorkerUrl(maplibreWorkerUrl); // once, at module scope, before any Map is created

const map = new MapLibreMap({
  container: containerRef.current!,
  style: MAP_STYLES[DEFAULT_MAP_STYLE_ID].url,
  center: [174.78, -41.29],
  zoom: 11,
  attributionControl: {
    customAttribution:
      '© <a href="https://www.linz.govt.nz/linz-copyright">LINZ CC BY 4.0</a>',
  },
});
```

**Marker**
```ts
import { Marker } from 'maplibre-gl';

new Marker({ element: el, offset: [0, 0] })
  .setLngLat([lon, lat])
  .addTo(map);
```
Heading rotation isn't done via the Marker's own `rotation` option in this codebase — see task 0.2's note (it's done inside the React `Arrow`/`Dot` component the marker renders). When heading is unavailable the client renders a plain dot, so keep that existing behaviour.

---

## Risks and notes

- The API key ends up in the browser bundle, like a Mapbox public token. Treat it as public. Don't reuse it for anything else.
- LINZ tracks usage and may restrict unreasonable use, and Basemaps can have scheduled outages. If the map matters during a live event, note this in the runbook.
- The style is a third-party file. If LINZ renames or changes tilesets, the map may change or break without any code change on our side.

---

## Progress log

_Add newest entries at the bottom. Format: `YYYY-MM-DD, machine/agent, what was done, what's next`._

- 2026-09-21, Claude Code (client-linz-maps branch), Completed 0.2 (mapped all mapbox-gl usage in the client) and 0.3 (confirmed `topographic-v2` is the current tileset via LINZ docs). Completed Phase 1 (swapped `mapbox-gl` for `maplibre-gl` everywhere in `client/src`, removed the access-token line, uninstalled `mapbox-gl`). Completed Phase 2 (added `src/map/mapStyle.ts`, pointed the map at `LINZ_TOPO_STYLE`, added `client/.env.example` — using `.env` not `.env.local` per existing project convention, updated CI workflow and `client/README.md`'s deploy instructions for `VITE_LINZ_API_KEY`). Completed Phase 3 (confirmed the style has no built-in attribution, added `customAttribution` via the `attributionControl` Map option, verified links; overlap-with-UI check is code-reasoned only, not on-device). Completed Phase 4.1/4.2 (no test mocks to update; CI needs only the env var rename, already done). Updated root `README.md`/`README.html` and `client/README.md` to say MapLibre/LINZ instead of Mapbox (Phase 6). Answered the "where is it deployed" open decision from existing docs (no human input needed); left the "outside NZ" decision on its stated default. What's next: task 0.1 (a human needs to request a real LINZ Developer API key — nothing here required the real key, only a placeholder), task 4.3 (open the PR — not done yet, pending confirmation), and Phase 5 (on-device manual check, needs a human with the real key).
- 2026-09-21, Claude Code (client-linz-maps branch), Human confirmed they've submitted the LINZ Developer API key request (0.1). Key itself not yet issued. What's next: once the key arrives, put it in `client/.env` and the Tatooine deploy `.env`, then run Phase 5 (on-device manual check); after that, task 4.3 (push and open the PR — human previously chose to hold off until Phase 5 is done).
- 2026-09-21, Claude Code (client-linz-maps branch), Human asked (a) whether they can test without the real Developer key, and (b) raised that `topographic-v2` reads poorly on urban routes and floated a basemap selector. Answered (a) in Facts (no-registration "Standard" key, no waiting needed). For (b), researched LINZ's actual style catalogue (no generic "streets" style exists — `aerialhybrid` real imagery is the closest fit for urban routes) and asked the human how to scope it; they chose to build it into this same branch/PR now. Added **Phase 7**: `MAP_STYLES` registry with `topo`/`aerial` entries in `mapStyle.ts`, a `MapStyleToggle` button next to `LegendHelp`, an imperative `map.setStyle()` toggle in `App.tsx` (not a map-recreate), and switched `useRouteLayer.ts`/`useSimulatorRouteOverlay.ts` from the one-shot `'load'` event to the recurring `'style.load'` event so the route line and simulator overlay survive a style swap. Noted that `aerialhybrid` carries its own source-level attribution, so both LINZ notices will show together while that style is active — left as-is. What's next: same as before (0.1's key, then Phase 5 including the new 5.7 toggle check, then 4.3), nothing here required the real key.
- 2026-09-21, Claude Code (client-linz-maps branch), Human ran `npm run dev` locally and reported "localhost isn't running" / blank page. Diagnosed live: (1) a stale/orphaned Vite process from earlier was already holding port 5173, silently pushing the real dev server to 5174 — killed the stale process, restarted cleanly on 5173. (2) Root cause of the actual blank page, from the browser console: `Uncaught SyntaxError: The requested module '/node_modules/.vite/deps/maplibre-gl.js...' does not provide an export named 'default'`. **The plan turned out to be wrong** (per "Rules for the agent"): `maplibre-gl@6.10.0` has no default export at all, only named ones — every file that did `import maplibregl from 'maplibre-gl'` (`App.tsx`, `useRunnerMarkers.ts`, `useRouteLayer.ts`, `useSimulatorRouteOverlay.ts`) was broken, and TypeScript's `esModuleInterop` hid it at compile time. Fixed by switching all four to named imports (`MapLibreMap`, `Marker`, `NavigationControl`, `GeolocateControl`, `LngLat`, `LngLatBounds`, `type GeoJSONSource`, `type AnimationOptions`); verified directly against the running dev server (curled the transformed source and the optimized dep bundle) that the fix resolves cleanly, not just that it compiles. Updated Facts, task 1.2, and the Map-creation/Marker code sketches to match. What's next: same outstanding items as before (0.1's key, Phase 5 including 5.7, then 4.3) — the human still needs to reload their browser tab to pick up the fix.
- 2026-09-21, Claude Code (client-linz-maps branch), Human reported the app now loads, but Aerial worked while Topo showed nothing but flat blue tiles. Diagnosed live with the human running DevTools checks on request: ruled out a LINZ/data problem first (fetched the live `topographic-v2.json` style, its `tile.json`, and an actual `.pbf` tile server-side via `curl` with the real key — style, sources, CORS, gzip enc all fine; parsed the tile with a throwaway `@mapbox/vector-tile`+`pbf` script in the scratchpad dir and confirmed it holds real, correctly-shaped `land`/`streets`/`contours`/etc. features). Network tab (after correcting the human's first two screenshots, which were Console not Network) showed **zero `.pbf` requests ever**, on *both* styles — Aerial only looked fine because its raster imagery masked the same missing vector overlay. Had the human run a couple of one-off console scripts against a temporarily-exposed `window.__map` (removed again after): `map.getSource('LINZ Basemaps').loaded()` was `true` (TileJSON metadata resolved fine) but `map.isStyleLoaded()` / `map.isSourceLoaded(...)` were `false` for *every* tiled source, including our own empty local `gpx-route`/`simulator-routes` GeoJSON sources — not LINZ-specific at all. That pointed at MapLibre's tile-loading worker never starting, confirmed via `maplibre-gl-js` GitHub issues #8018/#8459: **v6 needs an explicit `setWorkerUrl()` call that v5 didn't**, or the dispatcher hangs forever with no error. (Tested and ruled out React StrictMode double-mount first — reverted that dead end.) Fixed per the official v5→v6 migration guide's Vite snippet: `import maplibreWorkerUrl from 'maplibre-gl/dist/maplibre-gl-worker.mjs?worker&url'` + `setWorkerUrl(maplibreWorkerUrl)` at module scope in `App.tsx`, before any `Map` is created. Human confirmed real map tiles (roads, buildings, coastline) now render. Updated Facts, task 1.1, and the Map-creation code sketch. What's next: same outstanding items as before (0.1's key — human has one already for testing per the earlier Standard-tier note, Phase 5 including 5.5's note, then 4.3).
- 2026-09-21, Claude Code (client-linz-maps branch), Human confirmed they now have a working LINZ API key in `client/.env` (the one already exercised during the `setWorkerUrl` debugging session). Noted they'll copy it into the Tatooine deploy `.env` themselves at deploy time — that's already documented under task 2.4 and needs no code change, just the value. What's next: Phase 5 (on-device manual check, including 5.7's toggle check) is now fully unblocked — nothing left requires a key. Then task 4.3 (push and open the PR, still holding per the human's earlier answer).
