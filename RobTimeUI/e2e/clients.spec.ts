import { expect, test } from '@playwright/test'
import { signIn } from './signIn'

/**
 * The Phase 2 smoke test: log in, create a client, edit it, log out — plus delete, so each run
 * cleans up the row it created rather than accumulating test data in the dev database.
 *
 * This automates exactly the click-through that was previously done by hand, which is the point:
 * verification shouldn't depend on someone remembering to do it.
 */
test('create, rename, and delete a client', async ({ page }) => {
  // Unique per run — these tests share a real database, so a fixed name would collide with itself
  // on the second run.
  const name = `E2E Co ${Date.now()}`
  const renamed = `${name} Renamed`

  await signIn(page)

  await page.getByRole('link', { name: 'Setup' }).click()
  await page.getByRole('link', { name: 'Clients' }).click()
  await expect(page.getByRole('heading', { name: 'Clients' })).toBeVisible()

  // Create
  await page.getByRole('link', { name: 'New client' }).click()
  await page.getByLabel('Name').fill(name)
  await page.getByRole('button', { name: 'Create client' }).click()

  // Creating lands on the new client's own page — the heading is the client name.
  await expect(page.getByRole('heading', { name })).toBeVisible()

  // Rename. The update is optimistic, so the heading should reflect the new name without waiting on
  // a refetch — and still be correct after the server confirms.
  await page.getByLabel('Name').fill(renamed)
  await page.getByRole('button', { name: 'Save changes' }).click()
  await expect(page.getByRole('heading', { name: renamed })).toBeVisible()

  // Search finds it, and the filter is real URL state rather than component state.
  await page.getByRole('link', { name: '← Clients' }).click()
  await page.getByPlaceholder('Search by name…').fill(renamed)
  await expect(page).toHaveURL(/[?&]q=/)
  await expect(page.getByRole('link', { name: renamed })).toBeVisible()

  // Delete, via the inline confirmation.
  await page.getByRole('link', { name: renamed }).click()
  await page.getByRole('button', { name: 'Delete client' }).click()
  await page.getByRole('button', { name: 'Yes, delete' }).click()

  // Back on the list, and the soft-deleted client is gone from it.
  await expect(page.getByRole('heading', { name: 'Clients' })).toBeVisible()
  await page.getByPlaceholder('Search by name…').fill(renamed)
  await expect(page.getByRole('link', { name: renamed })).toBeHidden()
})

test('rejects an empty client name before calling the API', async ({ page }) => {
  await signIn(page)

  await page.goto('/setup/clients/new')
  await page.getByRole('button', { name: 'Create client' }).click()

  // Client-side Zod catches this; the server enforces the same rule as the backstop.
  await expect(page.getByText('Name is required.')).toBeVisible()
  await expect(page).toHaveURL(/\/setup\/clients\/new/)
})

test('signing out returns to the login screen and protects routes again', async ({ page }) => {
  await signIn(page)

  await page.getByRole('button', { name: 'Sign out' }).click()

  // Sign-out clears Cognito's session too, so this ends up back at our own login screen.
  await page.waitForURL((url) => url.origin === 'http://localhost:5173', { timeout: 30_000 })
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible({ timeout: 30_000 })

  // And the guard is live again: a protected route redirects rather than rendering.
  await page.goto('/setup/clients')
  await expect(page).toHaveURL(/\/login/)
})
