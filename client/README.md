# Client

React web app for viewing live runner positions on a Mapbox map.

## Prerequisites

- Node.js
- A Mapbox public access token ([mapbox.com](https://www.mapbox.com))

## Setup

Create a `.env` file in this directory:

```
VITE_MAPBOX_TOKEN=your_mapbox_public_token_here
VITE_SERVER_URL=http://localhost:5000
VITE_POLL_INTERVAL_MS=10000
```

Install dependencies (first time only):

```
npm install
```

## Running

```
npm run dev
```

The app will be available at `http://localhost:5173` by default.

## Environment variables

| Variable               | Required | Default                 | Description                                              |
| ---------------------- | -------- | ----------------------- | -------------------------------------------------------- |
| `VITE_MAPBOX_TOKEN`    | Yes      | —                       | Mapbox public access token for rendering the map         |
| `VITE_SERVER_URL`      | No       | `http://localhost:5000` | Base URL of the dot-watcher server                       |
| `VITE_POLL_INTERVAL_MS`| No       | `10000`                 | How often (ms) to poll the server for updated positions  |

## Usage

Open the app, sign in, then select an existing session, create one, or join from an invite code. Direct links such as `http://localhost:5173/SESSIONCODE` work after the signed-in user has membership for that session. Invite links use `/join/INVITECODE`.

## Auth and sessions

- Sign-in and account creation call `POST /auth/login` and `POST /auth/register`.
- The server returns `{ accessToken, expiresAt, user }`; the app keeps the access token in `sessionStorage`, clears older `localStorage` token keys, and calls `POST /auth/logout` on sign-out.
- After sign-in, the app loads `GET /me/sessions`. Users can open live or replay views only for returned memberships.
- Creating a session calls `POST /sessions` and stores the creator as `owner`.
- Joining from an invite calls `POST /session-invites/{inviteCode}/join`. Invite joins create `viewer` membership for new members and preserve existing roles; invite codes do not grant runner or owner privileges.
- Owners can open the member manager, load `GET /sessions/{sessionCode}/members`, and call `POST /sessions/{sessionCode}/members/{userId}/role` to promote viewers to runners or demote runners to viewers.
- A raw session code in the URL is only an identifier. If the user lacks membership, protected server endpoints return `403`.

## CI

GitHub Actions runs the `Client Node` workflow for client changes. It installs dependencies with `npm ci`, runs `npm run test`, and builds the Vite app with a placeholder Mapbox token. Local agents should not run those commands unless explicitly asked.

---

## Deployment (Tatooine — home k3s cluster)

The client runs on Tatooine, a home lab k3s cluster. It is deployed via the `/deploy` skill in Claude Code, which builds a Docker image (nginx serving the Vite static build), pushes it to the local registry at `localhost:5000`, and applies k8s manifests.

- **URL:** `http://dot-watcher.skelstar.io`
- **Namespace:** `dot-watcher-client`
- **Image:** `localhost:5000/dot-watcher-client:latest`
- **Manifests:** `/home/skelstar/deployments/dot-watcher-client/k8s/manifests.yaml`

The client shares the hostname `dot-watcher.skelstar.io` with the server. Traefik routes `/api/*` to the server pod and everything else to this pod. In production, set `VITE_SERVER_URL=/api` so the client calls the server via a relative URL on the same hostname.

### First-time deploy

The `client/.deploy.yaml` drives the deployment:

```yaml
name: dot-watcher-client
port: 80
hostname: dot-watcher.skelstar.io
```

The `client/Dockerfile` builds with yarn and serves via nginx. `nginx.conf` handles SPA client-side routing with `try_files`.

> **Important:** Vite bakes env vars into the JS bundle at build time — they cannot be injected at runtime. Before deploying, copy your `.env` into `/home/skelstar/deployments/dot-watcher-client/src/`:
>
> ```
> VITE_MAPBOX_TOKEN=your-mapbox-token
> VITE_SERVER_URL=/api
> ```

Then run in Claude Code:

```
/deploy https://github.com/skelstar/dot-watcher.git but just deploy the app from the /client folder
```

Add the DNS record in Unifi (Settings → Routing → DNS):
`dot-watcher.skelstar.io → 192.168.1.71`

(This record is shared with the server — only one DNS entry needed for both.)

### Updating after code changes

The `.env` is gitignored. On every update it must be re-copied into the source folder before the build runs:

```bash
cp /path/to/your/.env /home/skelstar/deployments/dot-watcher-client/src/.env
```

Then run in Claude Code:

```
/deploy update dot-watcher-client
```
