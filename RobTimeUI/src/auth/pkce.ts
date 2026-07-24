/**
 * PKCE (RFC 7636) helpers for the authorization-code flow.
 *
 * A SPA is a *public* client — it ships to the browser, so it can't hold a client secret. PKCE is
 * what replaces the secret: we send a hash of a random verifier up front, then prove we know the
 * original verifier when redeeming the code. An attacker who intercepts the authorization code
 * can't redeem it without the verifier, which never leaves this origin.
 */

function base64UrlEncode(bytes: Uint8Array): string {
  return btoa(String.fromCharCode(...bytes))
    .replace(/\+/g, '-')
    .replace(/\//g, '_')
    .replace(/=+$/, '')
}

function randomString(byteLength = 32): string {
  const bytes = new Uint8Array(byteLength)
  crypto.getRandomValues(bytes)
  return base64UrlEncode(bytes)
}

export function createVerifier(): string {
  return randomString()
}

export function createState(): string {
  return randomString(16)
}

export async function challengeFromVerifier(verifier: string): Promise<string> {
  const digest = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(verifier))
  return base64UrlEncode(new Uint8Array(digest))
}

// The verifier and state have to survive a full-page redirect to Cognito and back, so they can't
// live in memory. sessionStorage is the right tradeoff: it's per-tab, cleared when the tab closes,
// and these are single-use values that are consumed and deleted the moment the callback runs —
// unlike the tokens themselves, which deliberately never touch storage (see AuthProvider).
const VERIFIER_KEY = 'robtime.pkce.verifier'
const STATE_KEY = 'robtime.pkce.state'
const RETURN_TO_KEY = 'robtime.auth.returnTo'

export function stashFlowState(verifier: string, state: string, returnTo: string): void {
  sessionStorage.setItem(VERIFIER_KEY, verifier)
  sessionStorage.setItem(STATE_KEY, state)
  sessionStorage.setItem(RETURN_TO_KEY, returnTo)
}

export interface FlowState {
  verifier: string | null
  state: string | null
  returnTo: string
}

export function takeFlowState(): FlowState {
  const verifier = sessionStorage.getItem(VERIFIER_KEY)
  const state = sessionStorage.getItem(STATE_KEY)
  const returnTo = sessionStorage.getItem(RETURN_TO_KEY) ?? '/'
  sessionStorage.removeItem(VERIFIER_KEY)
  sessionStorage.removeItem(STATE_KEY)
  sessionStorage.removeItem(RETURN_TO_KEY)
  return { verifier, state, returnTo }
}
