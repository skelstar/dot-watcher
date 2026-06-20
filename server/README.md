# Server

.NET 9 minimal API for Dot Watcher. Receives GPS positions from the iOS app and serves them to web viewers. Latest live positions are held in memory; incoming positions and uploaded recordings are persisted to SQLite so sessions can be replayed later.

---

## Table of contents

- [Application structure](#application-structure)
- [Prerequisites](#prerequisites)
- [Configuration](#configuration)
- [Running locally](#running-locally)
- [API](#api)
  - [`POST /location`](#post-location)
  - [`GET /locations/{sessionCode}`](#get-locationssessioncode)
  - [`GET /sessions`](#get-sessions)
  - [`GET /sessions/{sessionCode}/recording`](#get-sessionssessioncoderecording)
  - [`DELETE /sessions/{sessionCode}`](#delete-sessionssessioncode)
- [Deployment](#deployment-tatooine--home-k3s-cluster)
- [Notes](#notes)

---

## Application structure

```text
                       +-----------------------+
                       |      iOS tracker      |
                       |  POST /location       |
                       |  Bearer token auth    |
                       +-----------+-----------+
                                   |
                                   v
+----------------------+   +-------+--------+   +----------------------+
| Web viewer / browser |-->| ASP.NET Core   |-->| SessionStore         |
|                      |   | controllers    |   |                      |
| GET /locations/{id}  |   | Program.cs     |   | In-memory live state |
| GET /sessions        |   | Controllers/*  |   | SQLite recordings    |
| GET /.../recording   |   | CORS enabled   |   | dotwatcher.db        |
+----------+-----------+   +-------+--------+   +----------+-----------+
           ^                       |                       ^
           |                       v                       |
           |              +---------------------+          |
           |              | wwwroot/index.html  |          |
           |              | Debug dashboard     |          |
           |              | /log + recordings   |          |
           |              +---------------------+          |
           |                                               |
           |              +---------------------+          |
           +--------------| Legacy migration    |----------+
                          | recordings/*.ndjson |
                          | imported on startup |
                          +---------------------+
```

`Program.cs` wires startup, static file hosting, CORS, controller routing, logging, and startup migration. `Controllers/*.cs` owns the HTTP endpoints. `Stores/SessionStore.cs` owns both the current in-memory session positions and the SQLite-backed recording history.

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

---

## Configuration

| Key | Default | Description |
| --- | --- | --- |
| `BearerToken` | *(required)* | Token used to authenticate `POST` and `DELETE` requests |
| `DbPath` | `dotwatcher.db` | SQLite database file used for persisted session recordings |
| `RecordingsPath` | `recordings` | Directory scanned on startup for legacy NDJSON recordings to import into SQLite |

The server requires a bearer token used to authenticate `POST` and `DELETE` requests from phone apps. Set it via:

**Environment variable (recommended for production):**

```
BearerToken=your-secret-token
```

**`appsettings.Development.json` (local only — already set to `dev-token`):**

```json
{
  "BearerToken": "dev-token"
}
```

The server will throw on startup if `BearerToken` is not configured.

> Do not commit a production token to source control.

---

## Running locally

```bash
cd server
dotnet run
```

`dotnet run` picks up `Properties/launchSettings.json`, which sets `ASPNETCORE_ENVIRONMENT=Development` so that `appsettings.Development.json` (with `BearerToken: dev-token`) is loaded automatically.

The server starts on `http://localhost:5000` by default. Override the port:

```bash
dotnet run --urls "http://localhost:8080"
```

Or set `ASPNETCORE_URLS=http://localhost:8080` as an environment variable.

> If `launchSettings.json` is not present (e.g. after a manual copy), set the environment explicitly:
> ```powershell
> $env:ASPNETCORE_ENVIRONMENT="Development"; dotnet run
> ```

---

## API

The API routes are implemented as ASP.NET Core controllers under `Controllers/`. Routes use absolute attributes so existing client URIs remain unchanged.

### `POST /location`

Receives a position update from a phone app.

**Auth:** `Authorization: Bearer <token>` header required.

**Request body:**

```json
{
  "runnerName": "Alice",
  "sessionCode": "SUNSET23",
  "latitude": -41.17,
  "longitude": 174.7762,
  "heading": 270.5,
  "timestamp": "2024-11-15T09:23:45Z"
}
```

| Field         | Type   | Required | Description                                               |
| ------------- | ------ | -------- | --------------------------------------------------------- |
| `runnerName`  | string | Yes      | Display name shown on the map                             |
| `sessionCode` | string | Yes      | Session identifier shared with viewers                    |
| `latitude`    | number | Yes      | WGS84 latitude                                            |
| `longitude`   | number | Yes      | WGS84 longitude                                           |
| `heading`     | number | No       | Compass bearing 0–360°, true north. Omit when unavailable |
| `timestamp`   | string | Yes      | ISO 8601 UTC timestamp                                    |

**Responses:**

| Status | Meaning                         |
| ------ | ------------------------------- |
| 200    | Position recorded               |
| 401    | Missing or invalid bearer token |

---

### `GET /locations/{sessionCode}`

Returns the latest live position for every runner in a session. Called by the web viewer.

**Auth:** None. The session code in the URL path is the only access control for read operations.

**Response body:**

Array of arrays — one inner array per runner, each containing that runner's latest live position.

```json
[
  [
    {
      "runnerName": "Alice",
      "latitude": -33.8688,
      "longitude": 151.2093,
      "heading": 270.5,
      "timestamp": "2024-11-15T09:23:45Z"
    }
  ],
  [
    {
      "runnerName": "Bob",
      "latitude": -33.8695,
      "longitude": 151.2101,
      "heading": null,
      "timestamp": "2024-11-15T09:23:30Z"
    }
  ]
]
```

Returns `[]` for an unknown session code.

**Responses:**

| Status | Meaning                      |
| ------ | ---------------------------- |
| 200    | Success (array may be empty) |

---

### `GET /sessions`

Lists all session codes that have a recording on disk, ordered newest first.

**Auth:** None.

**Response body:**

```json
["SUNSET23", "HILLTOP01"]
```

**Responses:**

| Status | Meaning |
| ------ | ------- |
| 200    | Success (array may be empty) |

---

### `GET /sessions/{sessionCode}/recording`

Downloads the full NDJSON recording for a session. Each line is one `LocationUpdate` JSON object in the order it was received.

**Auth:** None.

**Response:** `application/x-ndjson` file download named `{sessionCode}.ndjson`.

```json
{"runnerName":"Alice","sessionCode":"SUNSET23","latitude":-33.868,"longitude":151.209,"heading":268.0,"timestamp":"2024-11-15T09:23:25Z"}
{"runnerName":"Alice","sessionCode":"SUNSET23","latitude":-33.8684,"longitude":151.2091,"heading":269.5,"timestamp":"2024-11-15T09:23:35Z"}
```

**Responses:**

| Status | Meaning |
| ------ | ------- |
| 200    | File download |
| 404    | No recording exists for this session code |

---

### `DELETE /sessions/{sessionCode}`

Clears all position history for a session. Use this between runs.

**Auth:** `Authorization: Bearer <token>` header required.

**Responses:**

| Status | Meaning                            |
| ------ | ---------------------------------- |
| 204    | Session cleared (or did not exist) |
| 401    | Missing or invalid bearer token    |

---

## Deployment (Tatooine — home k3s cluster)

The server runs on Tatooine, a home lab k3s cluster. Deployments are managed via the `/deploy` skill in Claude Code, which builds a Docker image, pushes it to the local registry at `localhost:5000`, and applies k8s manifests.

- **URL:** `http://dot-watcher.skelstar.io/api`
- **Namespace:** `dot-watcher-server`
- **Image:** `localhost:5000/dot-watcher-server:latest`
- **Manifests:** `/home/skelstar/deployments/dot-watcher-server/k8s/manifests.yaml`

The server shares the hostname `dot-watcher.skelstar.io` with the client. Traefik routes `/api/*` requests here via a `StripPrefix` middleware, so the server still sees requests at `/location`, `/locations/{code}` etc. — no `/api` prefix in the code.

### Path-based routing

A `Middleware` resource in the manifest strips the `/api` prefix before forwarding to the pod:

```yaml
apiVersion: traefik.io/v1alpha1
kind: Middleware
metadata:
  name: strip-api-prefix
  namespace: dot-watcher-server
spec:
  stripPrefix:
    prefixes:
      - /api
```

Referenced from the `Ingress` via annotation:
```
traefik.ingress.kubernetes.io/router.middlewares: dot-watcher-server-strip-api-prefix@kubernetescrd
```

### First-time deploy

The `server/.deploy.yaml` at the root of this folder drives the deployment:

```yaml
name: dot-watcher-server
port: 8080
hostname: dot-watcher.skelstar.io
```

The `server/Dockerfile` is a two-stage build:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app
COPY . .
RUN dotnet publish -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "DotWatcher.Server.dll"]
```

Run in Claude Code:

```
/deploy https://github.com/skelstar/dot-watcher.git but just deploy the app from the /server folder
```

After the deploy completes, create the k8s secret for the bearer token (the value lives in `server/.env`, which is gitignored):

```bash
kubectl create secret generic dot-watcher-server-secrets \
  --from-env-file=server/.env \
  -n dot-watcher-server
```

Add the DNS record in Unifi (Settings → Routing → DNS):
`dot-watcher.skelstar.io → 192.168.1.71`

(This record is shared with the client — only one DNS entry needed for both.)

### Updating after code changes

Push changes to `main`, then run in Claude Code:

```
/deploy update dot-watcher-server
```

This re-clones the repo, rebuilds the image, pushes it to the local registry, and restarts the pod.

### BearerToken secret

The `BearerToken` is injected into the container via a k8s secret rather than committed to the repo. The secret is named `dot-watcher-server-secrets` in the `dot-watcher-server` namespace. To recreate it (e.g. after a namespace teardown):

```bash
kubectl create secret generic dot-watcher-server-secrets \
  --from-env-file=server/.env \
  -n dot-watcher-server
```

`server/.env` format:

```
BearerToken=your-secret-token
```

---

## Notes

- Restarting the server clears live session state (in-memory), but recordings on disk survive. After a restart, `GET /sessions` will still list past sessions and their recordings will still be downloadable.
- Recordings are **not** persisted across container redeployments by default — `dotwatcher.db` lives inside the container. Mount a volume for `DbPath` if you need recordings to survive deploys.
- The `timestamp` field in a `POST /location` request should be the **GPS capture time**, not the time the request was sent. Phone apps record the timestamp when the position fix is taken; the POST may be delayed or retried. Storing the capture time means the viewer always reflects where runners actually were at a given moment.
- Full position history is stored in SQLite per runner per session. The `GET /locations/{sessionCode}` endpoint returns only each runner's latest live position.
- CORS is open (`AllowAnyOrigin`) — appropriate for a private home lab deployment.
- Session codes are not validated beyond being present in the URL. An unknown code returns an empty array rather than a 404.
