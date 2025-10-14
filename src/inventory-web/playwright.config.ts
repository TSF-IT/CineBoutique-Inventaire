import { defineConfig, devices } from '@playwright/test'

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'https://127.0.0.1:4173'
const useExternalTarget = Boolean(process.env.PLAYWRIGHT_BASE_URL)

export default defineConfig({
  testDir: './tests/e2e',
  // On laisse plus de marge au premier rendu en CI
  timeout: 120_000,
  expect: {
    timeout: 10_000,
  },
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'retain-on-failure',
    video: 'retain-on-failure',
    screenshot: 'only-on-failure',

    // 👇 Tolère HTTPS auto-signé
    ignoreHTTPSErrors: true,

    // 👇 Quelques timeouts d'actions/navigation plus confortables en CI
    actionTimeout: 30_000,
    navigationTimeout: 30_000,

    // 👇 Force Chromium à ignorer les erreurs de certif (utile avec 127.0.0.1)
    launchOptions: {
      args: ['--ignore-certificate-errors'],
    },
  },
  ...(useExternalTarget
    ? {}
    : {
        webServer: {
          command: 'sh -c "npm run build && node tests/e2e/https-server.mjs"',
          url: 'https://127.0.0.1:4173',
          reuseExistingServer: !process.env.CI,
          timeout: 120_000,
          ignoreHTTPSErrors: true,
        },
      }),
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
})
