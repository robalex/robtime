import { expect, type Page } from '@playwright/test'

/**
 * Signs in through the real Cognito hosted UI.
 *
 * Credentials come from the environment and are never committed — see `.env.e2e.example`. Missing
 * credentials fail loudly rather than skipping: a silently-skipped auth test reports green while
 * testing nothing, which is worse than a red build that tells you why.
 */
export async function signIn(page: Page): Promise<void> {
  const email = process.env.E2E_EMAIL
  const password = process.env.E2E_PASSWORD

  if (!email || !password) {
    throw new Error(
      'E2E_EMAIL and E2E_PASSWORD must be set to run end-to-end tests. See RobTimeUI/.env.e2e.example.',
    )
  }

  await page.goto('/')

  // The root guard bounces an unauthenticated visitor here.
  await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  await page.getByRole('button', { name: 'Sign in' }).click()

  // Now on Cognito's domain. Its managed login is a two-step form (email, then password) but
  // collapses to one step in some configurations, so handle both rather than assuming.
  await page.waitForURL(/amazoncognito\.com/, { timeout: 30_000 })

  const emailField = page.getByRole('textbox').first()
  await emailField.waitFor({ state: 'visible', timeout: 30_000 })
  await emailField.fill(email)

  const passwordField = page.locator('input[type="password"]')
  if (await passwordField.isVisible().catch(() => false)) {
    await passwordField.fill(password)
  } else {
    await page.getByRole('button', { name: /continue|next|sign in/i }).first().click()
    await passwordField.waitFor({ state: 'visible', timeout: 30_000 })
    await passwordField.fill(password)
  }

  await page.getByRole('button', { name: /sign in|submit|continue/i }).first().click()

  // Back on the app, authenticated: the shell only renders its nav once /me has resolved, so this
  // asserts the whole chain (code exchange → token → API accepted it), not just the redirect.
  await page.waitForURL((url) => url.origin === 'http://localhost:5173', { timeout: 30_000 })
  await expect(page.getByRole('link', { name: 'Setup' })).toBeVisible({ timeout: 30_000 })
}
