import { expect, test } from '@playwright/test'

const requiresRealStack = !process.env.PLAYWRIGHT_BASE_URL

const SHOP_NAME = 'CinéBoutique Paris'
const USER_DISPLAY_NAME = 'Utilisateur Paris'
const MANUAL_EAN = '9900000001234'

test.describe('Parcours réel end-to-end', () => {
  test.skip(requiresRealStack, 'Ce test nécessite la stack réelle (PLAYWRIGHT_BASE_URL absent).')

  test('démarre un comptage et ajoute une ligne manuelle', async ({ page }) => {
    await page.goto('/')

    await page.waitForURL('**/select-shop', { waitUntil: 'domcontentloaded' })

    await page.getByRole('button', { name: SHOP_NAME }).click()
    await page.waitForURL('**/select-user', { waitUntil: 'domcontentloaded' })

    const userButton = page.getByRole('button', { name: new RegExp(USER_DISPLAY_NAME, 'i') })
    await userButton.click()

    const startButton = page.getByRole('button', { name: 'Débuter un comptage' })
    await expect(startButton).toBeVisible()
    await startButton.click()

    const locationPage = page.getByTestId('page-location')
    await expect(locationPage).toBeVisible()

    const firstZoneButton = page.getByTestId('btn-select-zone').first()
    await firstZoneButton.click()

    await page.waitForURL('**/inventory/count-type', { waitUntil: 'domcontentloaded' })

    const countTypePage = page.getByTestId('page-count-type')
    await expect(countTypePage).toBeVisible()

    const countOneButton = page.getByTestId('btn-count-type-1')
    await expect(countOneButton).toBeVisible()
    await countOneButton.click()

    await page.waitForURL('**/inventory/session', { waitUntil: 'domcontentloaded' })

    const sessionPage = page.getByTestId('page-session')
    await expect(sessionPage).toBeVisible()

    const scanInput = page.getByLabel('Scanner (douchette ou saisie)')
    await scanInput.fill(MANUAL_EAN)
    await scanInput.press('Enter')

    const manualButton = page.getByTestId('btn-open-manual')
    await expect(manualButton).toBeEnabled()

    await manualButton.click()

    const scannedItem = page.getByTestId('scanned-item').filter({ hasText: `EAN ${MANUAL_EAN}` })
    await expect(scannedItem).toBeVisible()

    const quantityInput = scannedItem.getByTestId('quantity-input')
    await expect(quantityInput).toHaveValue('1')

    const statusMessage = page.getByTestId('status-message')
    await expect(statusMessage).toContainText('ajouté manuellement')

    await expect(sessionPage.getByText(/\b1 références\b/)).toBeVisible()
  })
})
