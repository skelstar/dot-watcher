# Plan: LINZ topographic map tiles in the web client

> **Status:** in progress — Phases 0–4 code/doc work done except the human-gated steps below; Phase 5 (manual on-device check) and getting a real API key still need a human.
> **Suggested location in repo:** `client/docs/linz-topo-migration.md`
> **Resume on any machine:** open Claude Code in the repo root and paste:
> `Read client/docs/linz-topo-migration.md and continue from the first unchecked task. Follow the Rules for the agent section.`

---

## Goal

Replace the client's Mapbox basemap with the LINZ Basemaps topographic vector tiles so runners and watchers see a proper NZ topo map (contours, tracks, relief shading) instead of a generic street map.

Because LINZ recommends MapLibre for vector tiles and Mapbox GL JS still needs a Mapbox token and billing even with third-party styles, this means swapping `mapbox-gl` for `maplibre-gl`. The API is nearly identical, so runner markers, heading arrows and `fitBounds` should carry over with little or no change.

Only the map library and basemap style change. The server, iOS app, polling, auth and marker logic are out of scope.

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
- Licence: CC BY 4.0. The product must visibly show attribution and link to the LINZ copyright statement and the Basemap's custom attribution text. The mapping library does not do this automatically.
- Styling: tiles use the Shortbread schema. LINZ defines land polygons rather than ocean polygons, so in a custom style the background is water and land is drawn on top.
- Coverage is New Zealand only. Outside NZ the map will be blank or empty.
- An older tile.json listed a max zoom of 15. MapLibre overzooms past that, but check how it looks when zoomed in on a trail.

---

## Open decisions

- [ ] **[HUMAN]** Outside NZ: accept a blank map (NZ-only), or keep a Mapbox/OSM fallback with a style toggle? *Default if not answered: NZ-only, revisit later.* **Proceeding with the default (NZ-only)** since this wasn't answered — no fallback/toggle was built.
- [x] Where is the client built and deployed (GitHub Actions and Tatooine), so the build knows where to get `VITE_LINZ_API_KEY`? — **Answered from `client/README.md`, no human input needed:** GitHub Actions CI (build+test verification, placeholder key only) and Tatooine/k3s via the `/deploy` skill (real key goes in a gitignored `.env` copied to the host before deploy). See task 2.4.

---

## Task list

### Phase 0: Prep
- [ ] 0.1 **[HUMAN]** Request a free Developer API key at https://basemaps.linz.govt.nz (do not paste it into chat or commit it).
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
- [x] 1.2 Replace `mapbox-gl` imports with `maplibre-gl`, and the CSS import with `maplibre-gl/dist/maplibre-gl.css`.
  - Updated `App.tsx`, `useRunnerMarkers.ts`, `useRouteLayer.ts`, `useSimulatorRouteOverlay.ts` (import + every `mapboxgl.` → `maplibregl.` reference, including types like `maplibregl.Map`/`maplibregl.Marker`/`maplibregl.GeoJSONSource`). Also reworded stray "Mapbox" comments in `Legend.tsx`, `LegendHelp.tsx`, `useRunnerMarkers.ts`, `useRouteLayer.ts` for accuracy (none needed logic changes).
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
- [ ] 4.3 Open the PR. CI is the verification; do not run the toolchain locally.
  - Asked the human whether to push/open now vs. wait for 0.1 + Phase 5 first — chose to hold off. Commit `395a3bc` is local to `client-linz-maps` only; nothing pushed.

### Phase 5: Manual check **[HUMAN]**
- [ ] 5.1 Runners' dots and heading arrows render and update on the LINZ map.
- [ ] 5.2 Auto-fit to all runners still works.
- [ ] 5.3 Contours, tracks and labels are readable on a phone in daylight. If not, restyle in Maputnik and load a local style JSON instead.
- [ ] 5.4 Zoomed right in past z15, tiles still look acceptable.
- [ ] 5.5 Network tab shows no 401/403 tile errors.
- [ ] 5.6 Attribution is visible and both links work.

### Phase 6: Docs
- [x] 6.1 Update the root `README.md` and any client README that say "Mapbox GL JS" or "Mapbox" (the Components > client section, the structure summary).
  - Updated `README.md` (repo structure line, "how it works" step 5, Components > client intro + first bullet) and the generated-looking `README.html` (same three spots — it had already drifted slightly from `README.md` before this change, unrelated to this plan). `client/README.md` covered under 2.4/2.3 above. Remaining "Mapbox" mention is a deliberate historical comparison in the new env-var table row, not a leftover.
- [x] 6.2 Document `VITE_LINZ_API_KEY`, where to get one, and the NZ-only coverage note.
  - `client/README.md`: Prerequisites section names the free LINZ Basemaps Developer key and where to request it; the env var table's `VITE_LINZ_API_KEY` row states the NZ-only coverage note.

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
```ts
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';
import { LINZ_TOPO_STYLE } from './mapStyle';

const map = new maplibregl.Map({
  container: containerRef.current!,
  style: LINZ_TOPO_STYLE,
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
new maplibregl.Marker({ element: el, rotation: heading ?? 0 })
  .setLngLat([lon, lat])
  .addTo(map);
```
When heading is unavailable the client renders a plain dot, so keep that existing behaviour rather than rotating by 0.

---

## Risks and notes

- The API key ends up in the browser bundle, like a Mapbox public token. Treat it as public. Don't reuse it for anything else.
- LINZ tracks usage and may restrict unreasonable use, and Basemaps can have scheduled outages. If the map matters during a live event, note this in the runbook.
- The style is a third-party file. If LINZ renames or changes tilesets, the map may change or break without any code change on our side.

---

## Progress log

_Add newest entries at the bottom. Format: `YYYY-MM-DD, machine/agent, what was done, what's next`._

- 2026-09-21, Claude Code (client-linz-maps branch), Completed 0.2 (mapped all mapbox-gl usage in the client) and 0.3 (confirmed `topographic-v2` is the current tileset via LINZ docs). Completed Phase 1 (swapped `mapbox-gl` for `maplibre-gl` everywhere in `client/src`, removed the access-token line, uninstalled `mapbox-gl`). Completed Phase 2 (added `src/map/mapStyle.ts`, pointed the map at `LINZ_TOPO_STYLE`, added `client/.env.example` — using `.env` not `.env.local` per existing project convention, updated CI workflow and `client/README.md`'s deploy instructions for `VITE_LINZ_API_KEY`). Completed Phase 3 (confirmed the style has no built-in attribution, added `customAttribution` via the `attributionControl` Map option, verified links; overlap-with-UI check is code-reasoned only, not on-device). Completed Phase 4.1/4.2 (no test mocks to update; CI needs only the env var rename, already done). Updated root `README.md`/`README.html` and `client/README.md` to say MapLibre/LINZ instead of Mapbox (Phase 6). Answered the "where is it deployed" open decision from existing docs (no human input needed); left the "outside NZ" decision on its stated default. What's next: task 0.1 (a human needs to request a real LINZ Developer API key — nothing here required the real key, only a placeholder), task 4.3 (open the PR — not done yet, pending confirmation), and Phase 5 (on-device manual check, needs a human with the real key).
