import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import path from 'path'
import fs from 'fs'
import type { Plugin } from 'vite'

const ROUTES_DIR = path.resolve(__dirname, '../../data/routes')

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

export default defineConfig({
  plugins: [react(), saveRoutePlugin()],
  server: {
    port: 5174,
    fs: {
      allow: [path.resolve(__dirname, '../..')],
    },
  },
})
