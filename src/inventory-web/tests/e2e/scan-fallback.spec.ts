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

test.describe('Scanner fallback', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      Object.defineProperty(Navigator.prototype, 'mediaDevices', {
        configurable: true,
        get() {
          return undefined
        },
      })
      Object.defineProperty(window, 'isSecureContext', {
        configurable: true,
        value: false,
      })
    })

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
  })

  test('affiche le fallback photo lorsque la caméra est indisponible', async ({ page }) => {
    await page.goto('/inventory/start')

    await page.getByRole('button', { name: 'Amélie' }).click()
    await page.getByRole('button', { name: 'Comptage n°1' }).click()
    await page.getByRole('button', { name: 'Sélectionner la zone' }).click()
    await page.getByRole('button', { name: /Zone test/ }).first().click()

    await page.getByRole('button', { name: 'Activer la caméra' }).click()

    await expect(page.getByText('Caméra indisponible (connexion non sécurisée ou navigateur incompatible).')).toBeVisible()
    const fileInput = page.getByLabel('Importer une photo du code-barres')
    await expect(fileInput).toBeVisible()
    const inputType = await fileInput.getAttribute('type')
    expect(inputType).toBe('file')
  })
})
