import { createServer } from 'node:https'
import { promises as fs } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'

const __dirname = path.dirname(fileURLToPath(import.meta.url))
const projectRoot = path.resolve(__dirname, '../..')
const distDir = path.join(projectRoot, 'dist')
const certPath = path.join(__dirname, 'certs/localhost.pem')
const keyPath = path.join(__dirname, 'certs/localhost-key.pem')

const contentTypes = new Map([
  ['.html', 'text/html; charset=utf-8'],
  ['.js', 'application/javascript; charset=utf-8'],
  ['.css', 'text/css; charset=utf-8'],
  ['.json', 'application/json; charset=utf-8'],
  ['.svg', 'image/svg+xml'],
  ['.png', 'image/png'],
  ['.jpg', 'image/jpeg'],
  ['.jpeg', 'image/jpeg'],
  ['.ico', 'image/x-icon'],
  ['.txt', 'text/plain; charset=utf-8'],
  ['.webmanifest', 'application/manifest+json'],
  ['.woff2', 'font/woff2'],
  ['.woff', 'font/woff'],
])

const getContentType = (filePath) => contentTypes.get(path.extname(filePath).toLowerCase()) ?? 'application/octet-stream'

const readFileSafe = async (target) => {
  try {
    const data = await fs.readFile(target)
    return data
  } catch (error) {
    if ((error instanceof Error && 'code' in error && error.code === 'ENOENT') || (error && error.code === 'ENOENT')) {
      return null
    }
    throw error
  }
}

const start = async () => {
  const [cert, key] = await Promise.all([fs.readFile(certPath), fs.readFile(keyPath)])

  const server = createServer({ cert, key }, async (req, res) => {
    if (!req.url) {
      res.writeHead(400)
      res.end('Bad Request')
      return
    }

    const requestUrl = new URL(req.url, `https://${req.headers.host ?? 'localhost'}`)
    let pathname = decodeURIComponent(requestUrl.pathname)
    if (pathname.endsWith('/')) {
      pathname += 'index.html'
    }

    const candidate = path.normalize(path.join(distDir, pathname))
    if (!candidate.startsWith(distDir)) {
      res.writeHead(403)
      res.end('Forbidden')
      return
    }

    let payload = await readFileSafe(candidate)
    let servedPath = candidate
    if (!payload) {
      servedPath = path.join(distDir, 'index.html')
      payload = await readFileSafe(servedPath)
    }

    if (!payload) {
      res.writeHead(404)
      res.end('Not Found')
      return
    }

    res.writeHead(200, {
      'Content-Type': getContentType(servedPath),
      'Cache-Control': servedPath.includes('/assets/') ? 'public, max-age=31536000, immutable' : 'no-cache',
    })
    res.end(payload)
  })

  const port = Number(process.env.PORT ?? '4173')
  const host = process.env.HOST ?? '127.0.0.1'

  server.listen(port, host, () => {
    console.log(`HTTPS preview prêt sur https://${host}:${port}`)
  })
}

start().catch((error) => {
  console.error('Impossible de démarrer le serveur HTTPS de test', error)
  process.exit(1)
})
