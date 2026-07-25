import { defineConfig, devices } from '@playwright/test'

// Load .env.e2e if present (gitignored — see .env.e2e.example). Node's built-in loader, so no dotenv
// dependency. Absent file is fine: signIn() then fails with an actionable message about what to set,
// which is the same outcome by a clearer route than a missing-module crash here.
try {
  process.loadEnvFile('.env.e2e')
} catch {
  // No .env.e2e — credentials may still come from the shell or CI secrets.
}

/**
 * End-to-end smoke tests (UI_PLAN.md Phase 2).
 *
 * These drive the REAL stack: real Vite build, real API, real Postgres, and a real Cognito login.
 * Nothing is stubbed. That's a deliberate choice over the usual approach of mocking auth — the
 * alternative would mean shipping a test-only bypass in the app, and an auth bypass that exists in
 * the binary is exactly the kind of thing that escapes into an environment it shouldn't.
 *
 * The cost is that these need credentials (E2E_EMAIL / E2E_PASSWORD) and a reachable Cognito pool,
 * so they are NOT part of the default CI job. See README "End-to-end tests".
 *
 * Note also that Playwright's usual `storageState` trick for logging in once and reusing the session
 * doesn't apply here: tokens live in memory only (UI_PLAN.md §5), so there's nothing in storage to
 * save. Each test signs in through the hosted UI. Cognito's own session cookie makes repeat logins
 * within a run fast, since they become a redirect rather than a credential prompt.
 */
export default defineConfig({
  testDir: './e2e',
  // Serial: these share one dev database, and a client created by one test is visible to another.
  // Parallelism here would buy seconds and cost determinism.
  fullyParallel: false,
  workers: 1,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? 'github' : 'list',

  use: {
    baseURL: 'http://localhost:5173',
    // Only kept for failures — a passing smoke run shouldn't leave artifacts behind.
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
  },

  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],

  // Starts the whole stack. `reuseExistingServer` locally means an already-running dev server is
  // used as-is, so this doesn't fight the servers you have open while working.
  webServer: [
    {
      command: 'dotnet run --project ../TimeCalculation.Api --no-launch-profile --urls http://localhost:53534',
      url: 'http://localhost:53534/openapi/v1.json',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
      env: { ASPNETCORE_ENVIRONMENT: 'Development' },
      stdout: 'pipe',
    },
    {
      command: 'npm run dev',
      url: 'http://localhost:5173',
      reuseExistingServer: !process.env.CI,
      timeout: 120_000,
    },
  ],
})
