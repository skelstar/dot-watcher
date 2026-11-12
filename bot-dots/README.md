# Bot-dots service

Robot runners for DotWatcher. Each bot-dot is an ordinary `botdot-NNN` account that signs in, starts a casual
run and posts a position every 15 s, exactly like a phone, so people who have "show me to people nearby" on see
it as a grey dot (flagged `isBot`). Plan and decisions: [`.ai/plans/bot-dot-army.md`](../.ai/plans/bot-dot-army.md).
Route experiments: [`spike/`](spike/README.md).

## How it works

- **Where:** every 30 s it reads `GET /admin/casual/cells` (admin bearer): ~1 km cells (geohash length 6, about
  1.2 km by 0.6 km) that have real people out who can see grey dots. It never learns who or exactly where, but
  anyone in a cell is within ~700 m of its middle. A cell with 1 person gets 4 bot-dots, 2 people get 3, and so on
  up to 5 or more, who get none (`TargetPerCell` = 5). New ones arrive 2 per cell per sync, so they fill in over
  about a minute. When people leave or arrive, bot-dots finish their current hop and stop.
- **How it moves:** a bot-dot starts 150-1000 m from the cell's middle (so anywhere from a few hundred metres to
  about 1.5 km from a person) and wanders by hops of 300-800 m (Valhalla pedestrian routing), roughly ahead,
  avoiding ground it has covered and staying within 1.2 km of the middle. Near the edge it turns back. Pace comes from a persona (jogger, steady, walker) with a slow drift, and
  positions carry a few metres of wandering GPS error. A trip lasts 20-60 minutes.
- **Accounts:** a pool `botdot-001` ... `botdot-{MaxBots}`. Passwords are derived from the admin bearer, so
  nothing is stored and a restart signs straight back in. Initials are `XX`.
- **Off switch:** the server's `Bots:Enabled`. While it is off, the cells endpoint is 404, the service idles and
  the server draws no bot-dots even if some are still posting.

## Configuration (`BotDots` section, or environment `BotDots__Name`)

| Setting | Default | Notes |
| --- | --- | --- |
| `ServerUrl` | `http://localhost:8080` | Point it at **one** server: Staging and Production share a database. |
| `AdminBearer` | (required) | The server's `BearerToken`. From a secret, never a file. |
| `ValhallaUrl` | `http://valhalla:8002` | Self-hosted Valhalla with a New Zealand extract. |
| `ValhallaMinIntervalMs` | 0 | Set ~1100 when using the public demo server for local testing. |
| `MaxBots` | 60 | Fleet cap and account pool size. |
| `TargetPerCell` / `MaxPerCell` | 5 / 6 | See "Where" above. |
| `MaxSpawnPerCellPerSync` | 2 | Stagger, so a cell fills in over a few syncs. |
| `SpawnMinM` / `SpawnMaxM` / `MaxDistanceFromCentreM` | 150 / 1000 / 1200 | Metres from the cell's middle. |
| `HopMinM` / `HopMaxM` | 300 / 800 | |
| `TripMinMinutes` / `TripMaxMinutes` | 20 / 60 | |

## Running locally

1. Run the server as usual (`start-local.ps1`). `Bots:Enabled` is already on in
   `server/appsettings.Development.json`.
2. Run the service against it, using the public Valhalla demo politely. **On macOS, use Docker:** the demo
   only accepts TLS 1.3, which .NET on macOS can't do, so a native `dotnet run` fails every route (see
   Troubleshooting).

```
docker build -t dot-watcher-bot-dots bot-dots
docker run -d --name bot-dots-local --rm \
  -e BotDots__ServerUrl=http://host.docker.internal:8080 \
  -e BotDots__AdminBearer=dev-token \
  -e BotDots__ValhallaUrl=https://valhalla1.openstreetmap.de \
  -e BotDots__ValhallaMinIntervalMs=1100 \
  dot-watcher-bot-dots
docker logs -f bot-dots-local    # docker stop bot-dots-local to end it
```

   On Linux or Windows, `dotnet run` in `bot-dots/BotDots.Service` with the same `BotDots__` variables (and
   the default `ServerUrl`) works too.

3. Start a casual run on a phone or the simulator with "show me to people nearby" on. Within a minute or so,
   four bot-dots should appear, most within a kilometre or so. They start 150-1000 m from the cell's middle, and each
   stays hidden until it is 300 m from where it started (the usual start-area rule).

### Troubleshooting

- **`GET /admin/casual/cells` is 404** (the service logs "switched off on the server"): the server isn't
  running with `Bots:Enabled`, or it is an old build from another branch.
- **Cells show your run but `botDotCount` stays 0, and the server never sees `/auth/register`:** every route
  is failing. Route failures are logged at Debug only; run with `Logging__LogLevel__BotDots=Debug` to see
  them. An SSL "handshake failure" is the macOS TLS 1.3 problem above.

## Deploying (Tatooine)

- Its own Deployment, **one replica, `strategy: Recreate`**, no Service or ingress (outbound calls only).
  `.deploy.yaml` holds the non-secret environment; `BotDots__AdminBearer` comes from a secret.
- Valhalla runs beside it, e.g. the community `docker-valhalla` image pointed at Geofabrik's
  `new-zealand-latest.osm.pbf`, with a persistent volume for the built tiles (a few GB, built once).
- Not yet wired into `release.yml` (images are built per merge there); for now build from `bot-dots/` with the
  Dockerfile.
- Logs go to the console. Seq, as the server uses, is a follow-up.
