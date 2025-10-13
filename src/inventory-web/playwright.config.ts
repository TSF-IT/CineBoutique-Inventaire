import { defineConfig, devices } from '@playwright/test'

const baseURL = process.env.PLAYWRIGHT_BASE_URL ?? 'https://127.0.0.1:4173'
const useExternalTarget = Boolean(process.env.PLAYWRIGHT_BASE_URL)

export default defineConfig({
  testDir: './tests/e2e',
  timeout: 60_000,
  expect: {
    timeout: 5000,
  },
  fullyParallel: true,
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL,
    trace: 'on-first-retry',
    ignoreHTTPSErrors: true,
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
