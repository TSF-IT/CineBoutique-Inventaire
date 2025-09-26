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

    const userButton = page.getByRole('button', { name: 'Amélie' })
    await expect(userButton).toBeVisible()
    await userButton.click()

    const selectZoneButton = page.getByRole('button', { name: 'Sélectionner la zone' })
    await expect(selectZoneButton).toBeVisible()
    await selectZoneButton.click()

    await expect(page.getByTestId('page-location')).toBeVisible()

    const zoneCard = page.getByTestId(`zone-card-${mockLocations[0].id}`)
    await expect(zoneCard).toBeVisible()
    const zoneSelectButton = zoneCard.getByTestId('btn-select-zone')
    await expect(zoneSelectButton).toBeVisible()
    await zoneSelectButton.click()

    await expect(page).toHaveURL(/\/inventory\/count-type/)
    await expect(page.getByTestId('page-count-type')).toBeVisible()

    const btnCount1 = page.getByTestId('btn-count-type-1')
    await expect(btnCount1).toBeVisible()
    const isButtonDisabled = (await btnCount1.isDisabled()) || (await btnCount1.getAttribute('aria-disabled')) === 'true'
    if (isButtonDisabled) {
      if (await btnCount1.isDisabled()) {
        await expect(btnCount1).toBeDisabled()
      }
      if ((await btnCount1.getAttribute('aria-disabled')) === 'true') {
        await expect(btnCount1).toHaveAttribute('aria-disabled', 'true')
      }
      const btnCount2 = page.getByTestId('btn-count-type-2')
      await expect(btnCount2).toBeVisible()
      await btnCount2.click()
    } else {
      await btnCount1.click()
    }

    await expect(page).toHaveURL(/\/inventory\/session/)
    await expect(page.getByTestId('page-session')).toBeVisible()

    await page.getByRole('button', { name: 'Activer la caméra' }).click()

    await expect(page.getByText('Caméra indisponible (connexion non sécurisée ou navigateur incompatible).')).toBeVisible()
    const fileInput = page.getByLabel('Importer une photo du code-barres')
    await expect(fileInput).toBeVisible()
    const inputType = await fileInput.getAttribute('type')
    expect(inputType).toBe('file')
  })
})
