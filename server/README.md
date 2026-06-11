# Server

.NET 9 minimal API for Dot Watcher. Receives GPS positions from the iOS app and serves them to web viewers. All data is held in memory — no database.

---

## Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

---

## Configuration

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

The server starts on `http://localhost:5000` by default. Override the port:

```bash
dotnet run --urls "http://localhost:8080"
```

Or set `ASPNETCORE_URLS=http://localhost:8080` as an environment variable.

---

## API

### `POST /location`

Receives a position update from a phone app.

**Auth:** `Authorization: Bearer <token>` header required.

**Request body:**

```json
{
  "runnerName": "Alice",
  "sessionCode": "SUNSET23",
  "latitude": -33.8688,
  "longitude": 151.2093,
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

### `GET /positions/{sessionCode}`

Returns the latest position for every runner in a session. Called by the web viewer.

**Auth:** None. The session code in the URL path is the only access control for read operations.

**Response body:**

```json
[
  {
    "runnerName": "Alice",
    "latitude": -33.8688,
    "longitude": 151.2093,
    "heading": 270.5,
    "timestamp": "2024-11-15T09:23:45Z"
  },
  {
    "runnerName": "Bob",
    "latitude": -33.8695,
    "longitude": 151.2101,
    "heading": null,
    "timestamp": "2024-11-15T09:23:30Z"
  }
]
```

Returns `[]` for an unknown session code.

**Responses:**

| Status | Meaning                      |
| ------ | ---------------------------- |
| 200    | Success (array may be empty) |

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

## Deployment (k3s + Cloudflare Tunnel)

The server runs on Tatooine behind a Cloudflare Tunnel. Cloudflare handles TLS termination, so the container only speaks HTTP internally.

### Dockerfile

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DotWatcher.Server.dll"]
```

Build:

```bash
docker build -t dot-watcher-server:latest .
```

### Kubernetes manifests

Store the bearer token as a Secret:

```yaml
apiVersion: v1
kind: Secret
metadata:
  name: dot-watcher-secrets
stringData:
  bearer-token: your-secret-token-here
```

Deployment:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: dot-watcher-server
spec:
  replicas: 1
  selector:
    matchLabels:
      app: dot-watcher-server
  template:
    metadata:
      labels:
        app: dot-watcher-server
    spec:
      containers:
        - name: server
          image: dot-watcher-server:latest
          ports:
            - containerPort: 8080
          env:
            - name: ASPNETCORE_URLS
              value: http://+:8080
            - name: BearerToken
              valueFrom:
                secretKeyRef:
                  name: dot-watcher-secrets
                  key: bearer-token
```

---

## Notes

- Restarting the server clears all sessions — data is in-memory only.
- Full position history is stored per runner per session, not just the latest fix. The `GET /positions` endpoint returns only the latest position per runner.
- CORS is open (`AllowAnyOrigin`) — appropriate for a private deployment behind Cloudflare.
- Session codes are not validated beyond being present in the URL. An unknown code returns an empty array rather than a 404.
