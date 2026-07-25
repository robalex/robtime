/**
 * The SystemAdmin's currently-selected client (UI_PLAN.md §5).
 *
 * Stored in sessionStorage rather than localStorage, deliberately: per-tab scoping lets a
 * SystemAdmin keep two clients open side by side to compare configuration, and the selection can't
 * outlive the tab that made it. Read by the openapi-fetch middleware on every request, so it lives
 * in a module-level holder like the auth token does — middleware runs outside React.
 */
const STORAGE_KEY = 'robtime.selectedClientId'

export const selectedClientHolder: { current: number | null } = { current: readStoredSelection() }

function readStoredSelection(): number | null {
  const raw = sessionStorage.getItem(STORAGE_KEY)
  const parsed = raw === null ? Number.NaN : Number(raw)
  return Number.isInteger(parsed) ? parsed : null
}

export function getSelectedClientId(): number | null {
  return selectedClientHolder.current
}

export function setSelectedClientId(clientId: number | null): void {
  selectedClientHolder.current = clientId
  if (clientId === null) {
    sessionStorage.removeItem(STORAGE_KEY)
  } else {
    sessionStorage.setItem(STORAGE_KEY, String(clientId))
  }
}
