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

Open the app and enter a session code when prompted, or navigate directly to `http://localhost:5173/SESSIONCODE` to skip the prompt. The map will poll the server and render all runners in that session as directional markers.
