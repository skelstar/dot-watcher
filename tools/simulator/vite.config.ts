import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import fs from 'fs'
import { execSync } from 'child_process'
import type { Plugin } from 'vite'

const REPO_ROOT = path.resolve(__dirname, '../..')
const ROUTES_DIR = path.resolve(__dirname, 'data/current_route')

function saveRoutePlugin(): Plugin {
  return {
    name: 'save-route',
    configureServer(server) {
      server.middlewares.use('/api/save-route', async (req, res) => {
        if (req.method !== 'POST') { res.statusCode = 405; res.end(); return }

        const chunks: Buffer[] = []
        req.on('data', (chunk: Buffer) => chunks.push(chunk))
        req.on('end', () => {
          try {
            const { filename, content, force } = JSON.parse(Buffer.concat(chunks).toString())
            const dest = path.join(ROUTES_DIR, filename)

            if (!force && fs.existsSync(dest)) {
              res.statusCode = 409
              res.end(JSON.stringify({ exists: true }))
              return
            }

            fs.mkdirSync(ROUTES_DIR, { recursive: true })
            fs.writeFileSync(dest, content, 'utf8')
            res.statusCode = 200
            res.end(JSON.stringify({ saved: true }))
          } catch (e) {
            res.statusCode = 500
            res.end(JSON.stringify({ error: String(e) }))
          }
        })
      })
    },
  }
}

function simulatorPlugin(): Plugin {
  return {
    name: 'simulator-location',
    configureServer(server) {
      server.middlewares.use('/api/simulators', (req, res) => {
        res.setHeader('Content-Type', 'application/json')

        if (req.method === 'GET' && (req.url === '/' || req.url === '')) {
          try {
            const raw = execSync('xcrun simctl list devices --json').toString()
            const data = JSON.parse(raw) as { devices: Record<string, { udid: string; name: string; state: string }[]> }
            const booted = Object.entries(data.devices).flatMap(([runtime, devices]) =>
              devices
                .filter(d => d.state === 'Booted')
                .map(d => ({ udid: d.udid, name: d.name, runtime }))
            )
            res.end(JSON.stringify(booted))
          } catch (e) {
            res.statusCode = 500
            res.end(JSON.stringify({ error: String(e) }))
          }
          return
        }

        const locationMatch = req.url?.match(/^\/([^/]+)\/location$/)
        if (req.method === 'POST' && locationMatch) {
          const udid = locationMatch[1]
          const chunks: Buffer[] = []
          req.on('data', (chunk: Buffer) => chunks.push(chunk))
          req.on('end', () => {
            try {
              const { lat, lon } = JSON.parse(Buffer.concat(chunks).toString()) as { lat: number; lon: number }
              execSync(`xcrun simctl location ${udid} set ${lat},${lon}`, { stdio: 'pipe' })
              res.end(JSON.stringify({ ok: true }))
            } catch (e: unknown) {
              const err = e as { stderr?: Buffer }
              res.statusCode = 500
              res.end(JSON.stringify({ error: err.stderr?.toString() ?? String(e) }))
            }
          })
          return
        }

        res.statusCode = 404
        res.end(JSON.stringify({ error: 'not found' }))
      })
    },
  }
}

export default defineConfig(({ mode }) => {
  // Falls back to the repo root's .env so this agrees with start-local.ps1 and client/ on
  // which port the server is running on, without needing that value duplicated into this
  // tool's own .env alongside VITE_SESSION_CODE/VITE_BEARER_TOKEN.
  const rootEnv = loadEnv(mode, REPO_ROOT, '')

  return {
    plugins: [react(), saveRoutePlugin(), simulatorPlugin()],
    define: rootEnv.VITE_SERVER_PORT
      ? { 'import.meta.env.VITE_SERVER_PORT': JSON.stringify(rootEnv.VITE_SERVER_PORT) }
      : {},
    server: {
      port: 5174,
      fs: {
        allow: [REPO_ROOT],
      },
    },
  }
})
