import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { configDefaults } from 'vitest/config'
import devApiFixturesPlugin from './vite.dev-api-fixtures'

export default defineConfig({
  plugins: [react(), devApiFixturesPlugin()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:8080',
        changeOrigin: true,
        // on conserve /api -> /api (pas de rewrite)
        // rewrite: (path) => path, // inutile ici, juste pour clarifier
      },
    },
  },
  resolve: {
    alias: {
      '@': '/src',
    },
  },
  // durcit le build pour éviter l’explosion mémoire en CI/Docker
  build: {
    sourcemap: false, // désactivé en prod
    reportCompressedSize: false, // évite le calcul gzip (gourmand)
    chunkSizeWarningLimit: 1500, // évite les warnings inutiles
    // si tu utilises des images énormes, on pourra ajouter rollupOptions plus tard
  },
  test: {
    environment: 'jsdom',
    setupFiles: './setupTests.ts',
    exclude: [...configDefaults.exclude, 'tests/e2e/**'],
  },
})
