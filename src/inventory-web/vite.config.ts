import { fileURLToPath } from "node:url";
import path from "path";
import react from "@vitejs/plugin-react";
import { VitePWA } from "vite-plugin-pwa";
import { defineConfig, configDefaults } from "vitest/config";

const DEV_BACKEND_ORIGIN = (
  process.env.DEV_BACKEND_ORIGIN ?? "http://localhost:8080"
).trim();

const __dirname = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig(({ command }) => {
  return {
    plugins: [
      react(),
      VitePWA({
        registerType: "autoUpdate",
        includeAssets: ["icons/apple-touch-icon-180.png", "icons/pwa-icon.png"],
        workbox: {
          clientsClaim: true,
          skipWaiting: true,
          cleanupOutdatedCaches: true,
          navigateFallback: "/index.html",
          navigationPreload: true,
          globPatterns: ["**/*.{html,js,css,svg,png,ico,webmanifest,woff2}"],
          runtimeCaching: [
            {
              urlPattern: ({ request }) => request.mode === "navigate",
              handler: "NetworkFirst",
              options: {
                cacheName: "html",
                networkTimeoutSeconds: 3,
                expiration: { maxEntries: 3, maxAgeSeconds: 3600 },
              },
            },
            {
              urlPattern: ({ url }) => url.pathname.startsWith("/api/"),
              handler: "NetworkOnly",
            },
            {
              urlPattern: ({ request }) =>
                request.destination === "script" ||
                request.destination === "style" ||
                request.destination === "worker",
              handler: "StaleWhileRevalidate",
              options: { cacheName: "assets" },
            },
            {
              urlPattern: ({ request }) => request.destination === "image",
              handler: "StaleWhileRevalidate",
              options: {
                cacheName: "images",
                expiration: { maxEntries: 100, maxAgeSeconds: 604800 },
              },
            },
          ],
        },
        manifest: {
          name: "CinéBoutique – Inventaire",
          short_name: "Inventaire",
          start_url: "/",
          scope: "/",
          display: "standalone",
          background_color: "#0f172a",
          theme_color: "#0f172a",
          lang: "fr-FR",
          icons: [
            {
              src: "/icons/pwa-192x192.png",
              sizes: "192x192",
              type: "image/png",
            },
            {
              src: "/icons/pwa-512x512.png",
              sizes: "512x512",
              type: "image/png",
            },
            {
              src: "/icons/maskable-512.png",
              sizes: "512x512",
              type: "image/png",
              purpose: "maskable any",
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
        "/api": {
          target: DEV_BACKEND_ORIGIN,
          changeOrigin: false,
          secure: false,
          ws: true,
        },
        "/health": {
          target: DEV_BACKEND_ORIGIN,
          changeOrigin: false,
          secure: false,
        },
        "/healthz": {
          target: DEV_BACKEND_ORIGIN,
          changeOrigin: false,
          secure: false,
        },
        "/swagger": {
          target: DEV_BACKEND_ORIGIN,
          changeOrigin: false,
          secure: false,
        },
      },
    },
    resolve: {
      alias: [
        { find: "@", replacement: path.resolve(__dirname, "src") },
        ...(command === "serve"
          ? [
              {
                find: "react-window",
                replacement: path.resolve(
                  __dirname,
                  "src/shims/react-window.tsx"
                ),
              },
            ]
          : []),
      ],
    },
    build: {
      sourcemap: false,
      reportCompressedSize: false,
      chunkSizeWarningLimit: 1500,
      minify: "terser",
      target: "es2020",
      terserOptions: {
        compress: { passes: 2, pure_getters: true, ecma: 2020 },
        mangle: true,
        format: { comments: false },
      },
      rollupOptions: {
        output: {
          entryFileNames: "assets/[name]-[hash].js",
          chunkFileNames: "assets/[name]-[hash].js",
          assetFileNames: "assets/[name]-[hash][extname]",
          manualChunks: {
            react: ["react", "react-dom"],
            zod: ["zod"],
          },
        },
      },
    },
    test: {
      environment: "jsdom",
      setupFiles: ["./setupTests.ts"],
      exclude: [...configDefaults.exclude, "tests/e2e/**"],
      coverage: {
        provider: "v8",
        reporter: ["text", "lcov", "html"],
        include: ["src/app/**/*.{ts,tsx}"],
        thresholds: { lines: 80, branches: 70, functions: 80, statements: 80 },
      },
      pool: "threads",
      poolOptions: { threads: { minThreads: 1, maxThreads: 1 } },
    },
  };
});
