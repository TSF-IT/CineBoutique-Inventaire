import { defineConfig, configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { fileURLToPath, URL } from 'node:url'

// src/inventory-web/vite.config.ts
const DEV_BACKEND_ORIGIN =
  (process.env.DEV_BACKEND_ORIGIN ?? 'http://localhost:8080').trim()

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    // Vite sert en HTTP en dev. On proxifie vers l’API en HTTP aussi.
    // Laisse changeOrigin=false pour que l’API voie bien l’Host d’origine
    // et évite des comportements chelous côté Kestrel/CORS.
    proxy: {
      // Endpoints applicatifs
      '/api': {
        target: DEV_BACKEND_ORIGIN,
        changeOrigin: false,
        secure: false,
        ws: true,
      },

      // Health externes exposés par l’API (pratiques pour tester depuis le front)
      '/health': {
        target: DEV_BACKEND_ORIGIN,
        changeOrigin: false,
        secure: false,
      },
      '/healthz': {
        target: DEV_BACKEND_ORIGIN,
        changeOrigin: false,
        secure: false,
      },

      // Swagger et ses assets relatifs (/swagger/, /swagger/index.html, /swagger/v1/swagger.json)
      '/swagger': {
        target: DEV_BACKEND_ORIGIN,
        changeOrigin: false,
        secure: false,
      },
    },
  },
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  build: {
    sourcemap: false,
    reportCompressedSize: false,
    chunkSizeWarningLimit: 1500,
  },
  test: {
    environment: 'jsdom',
    setupFiles: ['./setupTests.ts'],
    exclude: [...configDefaults.exclude, 'tests/e2e/**'],
    // Vitest v2 : 1 seul worker pour éviter les flakies locales
    pool: 'threads',
    poolOptions: {
      threads: { minThreads: 1, maxThreads: 1 },
    },
  },
})
