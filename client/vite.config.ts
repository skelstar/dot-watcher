import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import { execSync } from 'node:child_process'
import path from 'node:path'

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')
  // The map style URL embeds this key at build time (src/map/mapStyle.ts). Without it LINZ answers 400 and
  // every map on the site is blank, with nothing to say why. A production build that is missing it is a
  // mistake, so stop it here rather than ship it. (CI builds with a placeholder, which is fine.)
  if (mode === 'production' && !env.VITE_LINZ_API_KEY) {
    throw new Error(
      'VITE_LINZ_API_KEY is not set, so the built site would have a blank map. Put it in client/.env ' +
        'or pass it as a build argument (docker build --build-arg VITE_LINZ_API_KEY=...).',
    )
  }
  // Falls back to the repo root's .env so this agrees with start-local.ps1 and
  // tools/simulator on which port the server is running on, without requiring the value to
  // also be duplicated into client/.env.
  const rootEnv = loadEnv(mode, path.resolve(__dirname, '..'), '')
  const appVersion = env.VITE_APP_VERSION ?? `v-${shortCommitId()}`
  const appUpdatedAt = env.VITE_APP_UPDATED_AT ?? `Updated ${formatNzBuildTime(new Date())}`
  const serverPort = env.VITE_SERVER_PORT ?? rootEnv.VITE_SERVER_PORT ?? '8080'

  return {
    plugins: [react()],
    define: {
      'import.meta.env.VITE_APP_VERSION': JSON.stringify(appVersion),
      'import.meta.env.VITE_APP_UPDATED_AT': JSON.stringify(appUpdatedAt),
    },
    server: {
      proxy: {
        '/api': {
          target: `http://localhost:${serverPort}`,
          changeOrigin: true,
          rewrite: (path) => path.replace(/^\/api/, ''),
        },
      },
    },
  }
})

function shortCommitId(): string {
  try {
    return execSync('git rev-parse --short=6 HEAD', { stdio: ['ignore', 'pipe', 'ignore'] })
      .toString()
      .trim()
  } catch {
    return 'local'
  }
}

function formatNzBuildTime(date: Date): string {
  const formatter = new Intl.DateTimeFormat('en-NZ', {
    timeZone: 'Pacific/Auckland',
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
    timeZoneName: 'short',
  })

  const parts = Object.fromEntries(
    formatter.formatToParts(date).map(part => [part.type, part.value]),
  )
  const timeZoneName = normalizeNzTimeZone(parts.timeZoneName ?? '')
  return `${parts.day} ${parts.month} ${parts.year} ${parts.hour}:${parts.minute} ${timeZoneName}`
}

function normalizeNzTimeZone(timeZoneName: string): string {
  if (timeZoneName === 'NZST' || timeZoneName === 'NZDT') {
    return timeZoneName
  }

  return timeZoneName.includes('13') ? 'NZDT' : 'NZST'
}
