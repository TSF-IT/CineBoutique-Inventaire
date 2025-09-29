/* eslint-env node */

import type { IncomingMessage, ServerResponse } from 'http'
import type { PluginOption } from 'vite'
import { cloneDevLocations } from './src/app/api/dev/fixtures'

const isGetRequest = (req: IncomingMessage) => (req.method ?? 'GET').toUpperCase() === 'GET'

const normalizeUrl = (req: IncomingMessage) => {
  const rawUrl = (req as IncomingMessage & { originalUrl?: string }).originalUrl ?? req.url ?? '/api/locations'
  try {
    return new URL(rawUrl, 'http://localhost')
  } catch {
    return new URL('http://localhost/api/locations')
  }
}

const buildResponsePayload = () => cloneDevLocations()

const respondWithJson = (res: ServerResponse, payload: unknown) => {
  res.statusCode = 200
  res.setHeader('Content-Type', 'application/json; charset=utf-8')
  res.end(JSON.stringify(payload))
}

export const devApiFixturesPlugin = (): PluginOption => ({
  name: 'dev-api-fixtures',
  apply: 'serve',
  configureServer(server) {
    const { env, logger } = server.config
    if (!env.DEV || env.VITE_DISABLE_DEV_FIXTURES === 'true') {
      return
    }

    server.middlewares.use('/api/locations', (req, res, next) => {
      if (!isGetRequest(req)) {
        next()
        return
      }

      const url = normalizeUrl(req)
      logger.info(`(dev fixtures) ${url.pathname}${url.search}`)
      respondWithJson(res, buildResponsePayload())
    })
  },
})

export default devApiFixturesPlugin
