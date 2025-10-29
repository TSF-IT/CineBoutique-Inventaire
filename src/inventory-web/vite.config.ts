import { fileURLToPath } from 'node:url'
import path from 'path'

import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'
import { defineConfig, configDefaults } from 'vitest/config'

// src/inventory-web/vite.config.ts
const DEV_BACKEND_ORIGIN =
  (process.env.DEV_BACKEND_ORIGIN ?? 'http://localhost:8080').trim()

const __dirname = path.dirname(fileURLToPath(import.meta.url))

export default defineConfig(({ command }) => {
  return {
    plugins: [
      react(),
      VitePWA({
        registerType: 'autoUpdate',
        workbox: {
          cleanupOutdatedCaches: true,
          globPatterns: ['**/*.{js,css,svg,png,ico,webmanifest,woff2}'],
          runtimeCaching: [
            {
              urlPattern: ({ request }) => request.mode === 'navigate',
              handler: 'NetworkFirst',
              options: { cacheName: 'html', networkTimeoutSeconds: 3 },
            },
            {
              urlPattern: ({ url }) => url.pathname.startsWith('/api/'),
              handler: 'NetworkOnly',
            },
            {
              urlPattern: ({ request }) =>
                request.destination === 'script' ||
                request.destination === 'style' ||
                request.destination === 'worker',
              handler: 'StaleWhileRevalidate',
              options: { cacheName: 'assets' },
            },
            {
              urlPattern: ({ request }) => request.destination === 'image',
              handler: 'StaleWhileRevalidate',
              options: {
                cacheName: 'images',
                expiration: { maxEntries: 100, maxAgeSeconds: 604800 },
              },
            },
          ],
        },
        manifest: {
          name: 'Cin\u00E9Boutique \u2013 Inventaire',
          short_name: 'Inventaire',
          start_url: '/',
          scope: '/',
          display: 'standalone',
          background_color: '#111',
          theme_color: '#111',
          icons: [
            { src: '/icons/pwa-192x192.png', sizes: '192x192', type: 'image/png' },
            { src: '/icons/pwa-512x512.png', sizes: '512x512', type: 'image/png' },
            {
              src: '/icons/maskable-512.png',
              sizes: '512x512',
              type: 'image/png',
              purpose: 'maskable any',
            },
          ],
        },
        devOptions: { enabled: false },
      }),
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
          entryFileNames: 'assets/[name]-[hash].js',
          chunkFileNames: 'assets/[name]-[hash].js',
          assetFileNames: 'assets/[name]-[hash][extname]',
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
