import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import path from 'node:path'

// Ajuste le port backend ci-dessous si tu lances dotnet run sur un port différent en dev.
const DEV_BACKEND_ORIGIN = process.env.DEV_BACKEND_ORIGIN || 'http://localhost:5000'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      manifest: {
        name: 'CinéBoutique – Inventaire',
        short_name: 'Inventaire',
        description: 'Application d\'inventaire mobile-first pour CinéBoutique.',
        theme_color: '#1f2937',
        background_color: '#111827',
        start_url: '/',
        display: 'standalone',
        lang: 'fr-FR',
        icons: [
          {
            src: 'https://img.icons8.com/fluency/192/clapperboard.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: 'https://img.icons8.com/fluency/512/clapperboard.png',
            sizes: '512x512',
            type: 'image/png',
          },
        ],
      },
    }),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  server: {
    host: true,
    port: 5173,
    strictPort: true,
    proxy: {
      // Redirige toutes les requêtes /api vers le backend local en dev.
      '/api': {
        target: DEV_BACKEND_ORIGIN,
        changeOrigin: true,
        secure: false,
        // rewrite keeps path as-is; si ton API attend /api/... ne pas décommenter.
        // rewrite: (path) => path.replace(/^\/api/, '')
      },
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: './setupTests.ts',
    coverage: {
      reporter: ['text', 'lcov'],
    },
  },
})
