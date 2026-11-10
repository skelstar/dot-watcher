# Bot-dots service

Robot runners for DotWatcher. Each bot-dot is an ordinary `botdot-NNN` account that signs in, starts a casual
run and posts a position every 15 s, exactly like a phone, so people who have "show me to people nearby" on see
it as a grey dot (flagged `isBot`). Plan and decisions: [`.ai/plans/bot-dot-army.md`](../.ai/plans/bot-dot-army.md).
Route experiments: [`spike/`](spike/README.md).

## How it works

- **Where:** every 30 s it reads `GET /admin/casual/cells` (admin bearer): ~5 km cells that have real people
  out who can see grey dots. It never learns who or exactly where. A cell with 1 person gets 2 bot-dots, 2 people
  get 1, 3 or more get none (`TargetPerCell` = 3). When people leave or arrive, bot-dots finish their current hop
  and stop.
- **How it moves:** a bot-dot starts 300-1800 m from the cell's middle and wanders by hops of 400-1200 m
  (Valhalla pedestrian routing), roughly ahead, avoiding ground it has covered and staying within 2 km of the
  middle. Near the edge it turns back. Pace comes from a persona (jogger, steady, walker) with a slow drift, and
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
| `MaxBots` | 40 | Fleet cap and account pool size. |
| `TargetPerCell` / `MaxPerCell` | 3 / 5 | See "Where" above. |
| `SpawnMinM` / `SpawnMaxM` / `MaxDistanceFromCentreM` | 300 / 1800 / 2000 | Metres from the cell's middle. |
| `HopMinM` / `HopMaxM` | 400 / 1200 | |
| `TripMinMinutes` / `TripMaxMinutes` | 20 / 60 | |

## Running locally

1. Run the server with `Bots__Enabled=true` (and Postgres, as usual).
2. Run the service against it, using the public Valhalla demo politely:

```
cd bot-dots/BotDots.Service
BotDots__AdminBearer=<your local BearerToken> \
BotDots__ValhallaUrl=https://valhalla1.openstreetmap.de \
BotDots__ValhallaMinIntervalMs=1100 \
dotnet run
```

3. Start a casual run on a phone or the simulator with "show me to people nearby" on. Within a minute, two
   bot-dots should appear within a few km.

## Deploying (Tatooine)

- Its own Deployment, **one replica, `strategy: Recreate`**, no Service or ingress (outbound calls only).
  `.deploy.yaml` holds the non-secret environment; `BotDots__AdminBearer` comes from a secret.
- Valhalla runs beside it, e.g. the community `docker-valhalla` image pointed at Geofabrik's
  `new-zealand-latest.osm.pbf`, with a persistent volume for the built tiles (a few GB, built once).
- Not yet wired into `release.yml` (images are built per merge there); for now build from `bot-dots/` with the
  Dockerfile.
- Logs go to the console. Seq, as the server uses, is a follow-up.
