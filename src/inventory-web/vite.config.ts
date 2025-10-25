import { defineConfig, configDefaults } from 'vitest/config'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import path from 'path'
import { fileURLToPath } from 'node:url'

const ICON_192_BASE64 =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAMAAAADACAYAAABS3GwHAAACGklEQVR4nO3TMQHAIADAsDEDnHjAvz+QwdFEQZ+Oufb5IOp/HQAvGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaQYgzQCkGYA0A5BmANIMQJoBSDMAaRfM9ALP7cf7tAAAAABJRU5ErkJggg=='

const ICON_512_BASE64 =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAgAAAAIACAYAAAD0eNT6AAAIYklEQVR4nO3WMQHAIADAsDEDnHjAvz/mYhxNFPTsmGufBwBIeW8HAAD/MwAAEGQAACDIAABAkAEAgCADAABBBgAAggwAAAQZAAAIMgAAEGQAACDIAABAkAEAgCADAABBBgAAggwAAAQZAAAIMgAAEGQAACDIAABAkAEAgCADAABBBgAAggwAAAQZAAAIMgAAEGQAACDIAABAkAEAgCADAABBBgAAggwAAAQZAAAIMgAAEGQAACDIAABAkAEAgCADAABBBgAAgj620AVPgMjaJAAAAABJRU5ErkJggg=='

const ICON_180_BASE64 =
  'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAALQAAAC0CAYAAAA9zQYyAAAB/UlEQVR4nO3SQQ3AIADAwDEDPPGAf3/MxBKS5k5BHx1z7fNAxHs7AP5kaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTYqhSTE0KYYmxdCkGJoUQ5NiaFIMTcoHCvsCt8GIO1EAAAAASUVORK5CYII='

// src/inventory-web/vite.config.ts
const DEV_BACKEND_ORIGIN =
  (process.env.DEV_BACKEND_ORIGIN ?? 'http://localhost:8080').trim()

const __dirname = path.dirname(fileURLToPath(import.meta.url))

export default defineConfig(({ command }) => {
  const isBuild = command === 'build'

  return {
    plugins: [
      react(),
      // Inclure la PWA seulement en build (pas d’option "apply" bidon)
      ...(isBuild
        ? [
            VitePWA({
              registerType: 'autoUpdate',
              includeAssets: ['favicon.ico', 'robots.txt'],
              manifest: {
                name: 'CineBoutique Inventaire',
                short_name: 'Inventaire',
                description: 'Application d’inventaire interne TSF',
                theme_color: '#0f172a',
                background_color: '#0f172a',
                display: 'standalone',
                scope: '/',
                start_url: '/',
                lang: 'fr-FR',
                icons: [
                  { src: ICON_180_BASE64, sizes: '180x180', type: 'image/png' },
                  { src: ICON_192_BASE64, sizes: '192x192', type: 'image/png' },
                  { src: ICON_512_BASE64, sizes: '512x512', type: 'image/png' },
                  { src: ICON_512_BASE64, sizes: '512x512', type: 'image/png', purpose: 'maskable' },
                ],
              },
              workbox: {
                clientsClaim: true,
                skipWaiting: true,
                cleanupOutdatedCaches: true,
                globDirectory: 'dist',
                globPatterns: ['**/*.{js,css,html,ico,png,svg,webp,woff2}'],
                navigateFallback: '/index.html',
                navigateFallbackDenylist: [
                  new RegExp('^/api'),
                  new RegExp('\\.(?:png|jpg|jpeg|svg|webp|gif)$'),
                ],
                runtimeCaching: [
                  {
                    urlPattern: ({ url }) =>
                      url.origin === self.location.origin && url.pathname.startsWith('/assets/'),
                    handler: 'CacheFirst',
                    options: {
                      cacheName: 'assets-static',
                      expiration: { maxEntries: 100, maxAgeSeconds: 60 * 60 * 24 * 30 },
                    },
                  },
                  {
                    urlPattern: ({ request }) => request.destination === 'image',
                    handler: 'StaleWhileRevalidate',
                    options: { cacheName: 'images' },
                  },
                  {
                    urlPattern: ({ url }) => url.pathname.startsWith('/api/'),
                    handler: 'NetworkOnly',
                  },
                ],
              },
              devOptions: {
                enabled: false, // no SW en dev
              },
            }),
          ]
        : []),
    ],
    server: {
      port: 5173,
      strictPort: true,
      proxy: {
        '/api': { target: DEV_BACKEND_ORIGIN, changeOrigin: false, secure: false, ws: true },
        '/health': { target: DEV_BACKEND_ORIGIN, changeOrigin: false, secure: false },
        '/healthz': { target: DEV_BACKEND_ORIGIN, changeOrigin: false, secure: false },
        '/swagger': { target: DEV_BACKEND_ORIGIN, changeOrigin: false, secure: false },
      },
    },
    resolve: {
      alias: [
        { find: '@', replacement: path.resolve(__dirname, 'src') },
        ...(command === 'serve'
          ? [
              {
                find: 'react-window',
                replacement: path.resolve(__dirname, 'src/shims/react-window.tsx'),
              },
            ]
          : []),
      ],
    },
    build: {
      sourcemap: false,
      reportCompressedSize: false,
      chunkSizeWarningLimit: 1500,
      minify: 'terser',
      target: 'es2020',
      terserOptions: {
        compress: { passes: 2, pure_getters: true, ecma: 2020 },
        mangle: true,
        format: { comments: false },
      },
      rollupOptions: {
        output: {
          manualChunks: {
            react: ['react', 'react-dom'],
            zod: ['zod'],
          },
        },
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./setupTests.ts'],
      exclude: [...configDefaults.exclude, 'tests/e2e/**'],
      coverage: {
        provider: 'v8',
        reporter: ['text', 'lcov', 'html'],
        include: [
          'src/app/components/Conflicts/**/*.{ts,tsx}',
          'src/app/components/Runs/**/*.{ts,tsx}',
          'src/app/components/inventory/**/*.{ts,tsx}',
          'src/app/components/ui/**/*.{ts,tsx}',
          'src/app/contexts/**/*.{ts,tsx}',
          'src/app/hooks/**/*.{ts,tsx}',
          'src/app/pages/admin/**/*.{ts,tsx}',
          'src/app/pages/home/**/*.{ts,tsx}',
          'src/app/pages/select-shop/**/*.{ts,tsx}',
          'src/app/providers/**/*.{ts,tsx}',
          'src/app/state/**/*.{ts,tsx}',
          'src/app/types/**/*.{ts,tsx}',
        ],
        exclude: [
          ...configDefaults.coverage.exclude,
          'tests/e2e/**',
          'src/**/e2e/**',
          'src/**/fixtures/**',
          'src/**/mocks/**',
          'src/**/*.d.ts',
          'src/**/*.stories.*',
        ],
        thresholds: {
          lines: 80,
          branches: 70,
          functions: 80,
          statements: 80,
        },
      },
      pool: 'threads',
      poolOptions: { threads: { minThreads: 1, maxThreads: 1 } },
    },
  }
})
