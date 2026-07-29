/**
 * Silent re-authentication via a hidden iframe, so a page reload can recover an existing Cognito
 * session without AuthProvider.signIn's visible full-page redirect-to-Cognito-and-back. Cognito's
 * hosted UI, when its own session cookie is still valid, redirects `/oauth2/authorize` straight back
 * to our redirect_uri with a fresh code — no login form is ever rendered — so doing that inside a
 * hidden iframe is invisible when it works. When the cookie is gone (a genuine logged-out visit),
 * Cognito would render the interactive login form instead; we never see it — the iframe simply never
 * posts back, and the timeout below is what tells the caller to fall back to the normal full-page
 * signIn.
 */

const MESSAGE_TYPE = 'robtime.auth.silent-callback'
const TIMEOUT_MS = 6000

export interface SilentAuthResult {
  code: string
  state: string
}

interface SilentAuthMessage {
  type: typeof MESSAGE_TYPE
  code?: string
  state?: string
  error?: string
}

function isSilentAuthMessage(data: unknown): data is SilentAuthMessage {
  return typeof data === 'object' && data !== null && (data as { type?: unknown }).type === MESSAGE_TYPE
}

/** Posts the redirect's code/state (or error) up to the parent window — called from auth.callback.tsx
 * when that route detects it's running inside this iframe, never from a top-level page load. */
export function postSilentCallbackResult(params: { code?: string; state?: string; error?: string }): void {
  const message: SilentAuthMessage = { type: MESSAGE_TYPE, ...params }
  window.parent.postMessage(message, window.location.origin)
}

/** Returns the code/state Cognito redirected back with, or null if the attempt errored out or the
 * hosted UI never redirected within the timeout (no valid session cookie — genuinely logged out). */
export function silentAuthorize(authorizeUrl: string): Promise<SilentAuthResult | null> {
  return new Promise((resolve) => {
    let settled = false
    const iframe = document.createElement('iframe')
    iframe.style.display = 'none'
    iframe.setAttribute('aria-hidden', 'true')

    function cleanup() {
      window.removeEventListener('message', onMessage)
      clearTimeout(timer)
      iframe.remove()
    }

    function settle(result: SilentAuthResult | null) {
      if (settled) {
        return
      }
      settled = true
      cleanup()
      resolve(result)
    }

    function onMessage(event: MessageEvent) {
      // Never trust postMessage without an origin check — this iframe only ever navigates to
      // Cognito and then back to our own origin, so anything else isn't a redirect result we sent.
      if (event.origin !== window.location.origin || !isSilentAuthMessage(event.data)) {
        return
      }
      const { code, state, error } = event.data
      settle(!error && code && state ? { code, state } : null)
    }

    const timer = setTimeout(() => settle(null), TIMEOUT_MS)

    window.addEventListener('message', onMessage)
    iframe.src = authorizeUrl
    document.body.appendChild(iframe)
  })
}
