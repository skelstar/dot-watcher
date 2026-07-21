import { defineConfig, loadEnv } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import fs from 'fs'
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

export default defineConfig(({ mode }) => {
  // Falls back to the repo root's .env so this agrees with start-local.ps1 and client/ on
  // which port the server is running on, without needing that value duplicated into this
  // tool's own .env.
  const rootEnv = loadEnv(mode, REPO_ROOT, '')

  return {
    plugins: [react(), saveRoutePlugin()],
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
