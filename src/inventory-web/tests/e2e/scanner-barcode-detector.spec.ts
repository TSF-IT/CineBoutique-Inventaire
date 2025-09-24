import { test, expect } from '@playwright/test'

const mockLocations = [
  {
    id: '11111111-1111-4111-8111-111111111111',
    code: 'Z1',
    label: 'Zone test',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: 1,
    activeStartedAtUtc: null,
  },
]

const simulatedEan = '5901234123457'

test.describe('Scanner avec BarcodeDetector', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(({ ean }) => {
      Object.defineProperty(window, 'isSecureContext', {
        configurable: true,
        value: true,
      })

      const track = {
        kind: 'video',
        stop: () => {},
        applyConstraints: async () => {},
        getCapabilities: () => ({ torch: true }),
        getSettings: () => ({ deviceId: 'mock-device' }),
      }
      const stream = {
        id: 'mock-stream',
        active: true,
        getTracks: () => [track],
        getVideoTracks: () => [track],
        addEventListener: () => {},
        removeEventListener: () => {},
      }

      Object.defineProperty(navigator, 'mediaDevices', {
        configurable: true,
        value: {
          getUserMedia: async () => stream,
        },
      })

      Object.defineProperty(HTMLVideoElement.prototype, 'play', {
        configurable: true,
        value: () => Promise.resolve(),
      })
      Object.defineProperty(HTMLMediaElement.prototype, 'srcObject', {
        configurable: true,
        set(value) {
          this.__srcObject = value
        },
        get() {
          return this.__srcObject
        },
      })
      Object.defineProperty(HTMLVideoElement.prototype, 'readyState', {
        configurable: true,
        get() {
          return 4
        },
      })
      Object.defineProperty(HTMLVideoElement.prototype, 'videoWidth', {
        configurable: true,
        get() {
          return 640
        },
      })
      Object.defineProperty(HTMLVideoElement.prototype, 'videoHeight', {
        configurable: true,
        get() {
          return 480
        },
      })

      class FakeBarcodeDetector {
        detect = async () => [{ rawValue: ean, format: 'ean_13' as const }]
      }

      Object.defineProperty(window, 'BarcodeDetector', {
        configurable: true,
        value: FakeBarcodeDetector,
      })
    }, { ean: simulatedEan })

    await page.route('**/api/locations**', async (route, request) => {
      if (request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify(mockLocations),
        })
        return
      }
      await route.fallback()
    })

    await page.route(`**/api/products/${simulatedEan}`, async (route, request) => {
      if (request.method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({ ean: simulatedEan, name: 'Produit simulé' }),
        })
        return
      }
      await route.fallback()
    })
  })

  test('déclenche onDetected via BarcodeDetector', async ({ page }) => {
    await page.goto('/inventory/start')

    await page.getByRole('button', { name: 'Amélie' }).click()
    await page.getByRole('button', { name: 'Comptage n°1' }).click()
    await page.getByRole('button', { name: 'Sélectionner la zone' }).click()
    await page.getByRole('button', { name: /Zone test/ }).first().click()

    await page.getByRole('button', { name: 'Activer la caméra' }).click()

    await expect(page.getByText('Produit simulé ajouté')).toBeVisible({ timeout: 5000 })
    await expect(page.getByText(`EAN ${simulatedEan}`)).toBeVisible()
    await expect(page.getByRole('listitem').filter({ hasText: simulatedEan })).toBeVisible()
  })
})
