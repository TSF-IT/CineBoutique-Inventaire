import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import path from 'node:path'

export default defineConfig(() => {
  const target = process.env.DEV_BACKEND_ORIGIN || 'http://localhost:8080'
  console.log(`[DEV] Vite proxy target = ${target}`)

  return {
    plugins: [
      react(),
      VitePWA({
        registerType: 'autoUpdate',
        manifest: {
          name: 'CinéBoutique – Inventaire',
          short_name: 'Inventaire',
          description: "Application d'inventaire mobile-first pour CinéBoutique.",
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
        '/api': {
          target,
          changeOrigin: true,
          secure: false,
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
  }
})
