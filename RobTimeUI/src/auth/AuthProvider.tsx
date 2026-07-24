import { createContext, use, useCallback, useMemo, useState, type ReactNode } from 'react'
import { authorizeUrl, getCognitoConfig, logoutUrl, tokenUrl } from './config'
import { challengeFromVerifier, createState, createVerifier, stashFlowState, takeFlowState } from './pkce'

/**
 * Holds the session. Tokens are kept in memory only — never localStorage or a cookie readable by
 * script (UI_PLAN.md §5: "bearer tokens in localStorage are readable by any XSS"). The cost is that
 * a page refresh drops the session; the mitigation is that Cognito keeps its own session cookie, so
 * re-authenticating is a redirect round-trip with no credential re-entry, not a real login.
 *
 * WHICH TOKEN GOES TO THE API — a deliberate choice, not an oversight. We send the **ID token**.
 * Cognito omits custom attributes from access tokens unless you pay for the Essentials tier's
 * access-token customization (or add a pre-token-generation Lambda), and this API's entire
 * authorization model reads `custom:role` and `custom:client_id` off the token. The API is
 * configured to match: `Audience = Cognito:UserPoolClientId` validates an ID token's `aud`. Sending
 * ID tokens to an API is normally discouraged, so the exit path is worth naming: add a
 * pre-token-generation Lambda that projects those two claims onto the access token, then switch both
 * sides to the access token in one change.
 */
interface AuthContextValue {
  idToken: string | null
  isAuthenticated: boolean
  signIn: (returnTo?: string) => Promise<void>
  signOut: () => void
  completeSignIn: (code: string, state: string) => Promise<string>
}

const AuthContext = createContext<AuthContextValue | null>(null)

/** Current ID token, for the openapi-fetch middleware — see the assignment in AuthProvider. */
export const tokenHolder: { current: string | null } = { current: null }

export function AuthProvider({ children }: { children: ReactNode }) {
  const [idToken, setIdToken] = useState<string | null>(null)

  // The openapi-fetch middleware runs outside React and can't read context, so the current token is
  // mirrored into a module-level holder. Assigning during render is safe here: it's idempotent and
  // derived purely from state, so a re-render can only ever write the same value again.
  tokenHolder.current = idToken

  const signIn = useCallback(async (returnTo = window.location.pathname) => {
    const config = getCognitoConfig()
    const verifier = createVerifier()
    const state = createState()
    stashFlowState(verifier, state, returnTo)
    window.location.assign(authorizeUrl(config, state, await challengeFromVerifier(verifier)))
  }, [])

  const completeSignIn = useCallback(async (code: string, returnedState: string) => {
    const config = getCognitoConfig()
    const flow = takeFlowState()

    // The state check is the CSRF defence for the callback: without it, an attacker could feed the
    // app an authorization code they obtained, logging the victim into the attacker's account.
    if (!flow.state || flow.state !== returnedState) {
      throw new Error('Authentication state mismatch — the sign-in attempt could not be verified.')
    }
    if (!flow.verifier) {
      throw new Error('Missing PKCE verifier — start the sign-in again.')
    }

    const response = await fetch(tokenUrl(config), {
      method: 'POST',
      headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
      body: new URLSearchParams({
        grant_type: 'authorization_code',
        client_id: config.clientId,
        code,
        redirect_uri: config.redirectUri,
        code_verifier: flow.verifier,
      }),
    })

    if (!response.ok) {
      throw new Error(`Token exchange failed (${response.status}).`)
    }

    const tokens: { id_token?: string } = await response.json()
    if (!tokens.id_token) {
      throw new Error('Token response contained no id_token.')
    }

    setIdToken(tokens.id_token)
    return flow.returnTo
  }, [])

  const signOut = useCallback(() => {
    setIdToken(null)
    // Cognito's own session cookie has to be cleared too — dropping the local token alone would
    // leave the next sign-in silently re-authenticating the same user with no prompt.
    window.location.assign(logoutUrl(getCognitoConfig()))
  }, [])

  const value = useMemo<AuthContextValue>(
    () => ({ idToken, isAuthenticated: idToken !== null, signIn, signOut, completeSignIn }),
    [idToken, signIn, signOut, completeSignIn],
  )

  return <AuthContext value={value}>{children}</AuthContext>
}

export function useAuth(): AuthContextValue {
  const context = use(AuthContext)
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider.')
  }
  return context
}
