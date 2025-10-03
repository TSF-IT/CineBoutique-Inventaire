import { test, expect } from '@playwright/test'

const testShop = {
  id: '00000000-0000-4000-8000-0000000000f1',
  name: 'Cinéma test',
}

const mockLocations = [
  {
    id: '11111111-1111-4111-8111-222222222222',
    code: 'Z1',
    label: 'Zone test',
    isBusy: false,
    busyBy: null,
    activeRunId: null,
    activeCountType: 1,
    activeStartedAtUtc: null,
  },
]

const productsByEan: Record<string, { ean: string; name: string }> = {
  '001': { ean: '001', name: 'Produit 001' },
  '0000': { ean: '0000', name: 'Produit 0000' },
}

test.describe("Ordre d'affichage des articles scannés", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(({ key, value }) => {
      window.localStorage.setItem(key, value)
    }, { key: 'cb.shop', value: JSON.stringify(testShop) })

    await page.route('**/api/inventories/**/start', async (route, request) => {
      if (request.method() !== 'POST') {
        await route.fallback()
        return
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          runId: '00000000-0000-4000-8000-000000000001',
          inventorySessionId: '00000000-0000-4000-8000-000000000002',
          locationId: mockLocations[0].id,
          countType: 1,
          operatorDisplayName: 'Amélie',
          startedAtUtc: new Date().toISOString(),
        }),
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

    await page.route('**/api/products/**', async (route, request) => {
      if (request.method() !== 'GET') {
        await route.fallback()
        return
      }

      const url = new URL(request.url())
      const ean = url.pathname.split('/').pop() ?? ''
      const product = productsByEan[ean]

      if (!product) {
        await route.fulfill({ status: 404, contentType: 'application/json', body: JSON.stringify({}) })
        return
      }

      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(product),
      })
    })
  })

  test('maintient un ordre stable lors des ajustements de quantité', async ({ page }) => {
    await page.goto('/inventory/start')

    const userButton = page.getByRole('button', { name: 'Amélie' })
    await expect(userButton).toBeVisible({ timeout: 5000 })
    await userButton.click()

    await expect(page).toHaveURL(/\/inventory\/location/, { timeout: 5000 })
    const zoneCard = page.getByTestId(`zone-card-${mockLocations[0].id}`)
    await expect(zoneCard).toBeVisible({ timeout: 5000 })
    await zoneCard.getByTestId('btn-select-zone').click()

    await expect(page).toHaveURL(/\/inventory\/count-type/, { timeout: 5000 })
    const countTypeButton = page.getByTestId('btn-count-type-1')
    if (await countTypeButton.isDisabled()) {
      await page.getByTestId('btn-count-type-2').click()
    } else {
      await countTypeButton.click()
    }

    await page.evaluate(() => {
      window.history.pushState({}, '', '/inventory/session')
      window.dispatchEvent(new PopStateEvent('popstate'))
    })

    await expect(page).toHaveURL(/\/inventory\/session/, { timeout: 5000 })

    const scannerInput = page.getByLabel('Scanner (douchette ou saisie)')

    await scannerInput.fill('001')
    await expect(page.locator('[data-testid="scanned-item"][data-ean="001"]')).toBeVisible({ timeout: 5000 })

    await scannerInput.fill('0000')
    await expect(page.locator('[data-testid="scanned-item"][data-ean="0000"]')).toBeVisible({ timeout: 5000 })

    const firstRowEan = await page.getByTestId('scanned-item').nth(0).getAttribute('data-ean')
    const secondRowEan = await page.getByTestId('scanned-item').nth(1).getAttribute('data-ean')
    expect(firstRowEan).toBe('001')
    expect(secondRowEan).toBe('0000')

    const secondRow = page.getByTestId('scanned-item').nth(1)

    await secondRow.getByRole('button', { name: 'Ajouter' }).click()
    await expect(secondRow.getByText('2')).toBeVisible({ timeout: 5000 })

    const firstRowAfterIncrement = await page.getByTestId('scanned-item').nth(0).getAttribute('data-ean')
    const secondRowAfterIncrement = await page.getByTestId('scanned-item').nth(1).getAttribute('data-ean')
    expect(firstRowAfterIncrement).toBe('001')
    expect(secondRowAfterIncrement).toBe('0000')

    await secondRow.getByRole('button', { name: 'Retirer' }).click()
    await expect(secondRow.getByText('1')).toBeVisible({ timeout: 5000 })

    const firstRowAfterDecrement = await page.getByTestId('scanned-item').nth(0).getAttribute('data-ean')
    const secondRowAfterDecrement = await page.getByTestId('scanned-item').nth(1).getAttribute('data-ean')
    expect(firstRowAfterDecrement).toBe('001')
    expect(secondRowAfterDecrement).toBe('0000')
  })
})
