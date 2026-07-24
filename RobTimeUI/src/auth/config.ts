/**
 * Cognito configuration, supplied via Vite env vars. Real values live in `.env.local`, which is
 * gitignored (`*.local`) — `.env.example` documents the shape. None of these are secrets: the app
 * client is a public SPA client with no client secret, which is exactly why the PKCE flow in
 * `pkce.ts` is required rather than optional.
 */
export interface CognitoConfig {
  /** e.g. `robtime-dev.auth.us-east-1.amazoncognito.com` — the managed login (Hosted UI) domain. */
  domain: string
  clientId: string
  /** Must exactly match a callback URL registered on the app client. */
  redirectUri: string
  logoutUri: string
}

function required(value: string | undefined, name: string): string {
  if (!value) {
    throw new Error(
      `Missing ${name}. Copy .env.example to .env.local and fill in your Cognito values — see RobTimeUI/README.md.`,
    )
  }
  return value
}

export function getCognitoConfig(): CognitoConfig {
  const origin = window.location.origin
  return {
    domain: required(import.meta.env.VITE_COGNITO_DOMAIN, 'VITE_COGNITO_DOMAIN'),
    clientId: required(import.meta.env.VITE_COGNITO_CLIENT_ID, 'VITE_COGNITO_CLIENT_ID'),
    // Derived from the current origin rather than configured separately, so the value can't drift
    // out of sync with wherever the app is actually being served from. Whatever this resolves to
    // must be registered on the app client, or Cognito refuses the redirect.
    redirectUri: `${origin}/auth/callback`,
    logoutUri: `${origin}/`,
  }
}

/** Cognito's OAuth endpoints, derived from the managed-login domain. */
export function authorizeUrl(config: CognitoConfig, state: string, codeChallenge: string): string {
  const params = new URLSearchParams({
    client_id: config.clientId,
    response_type: 'code',
    scope: 'openid email profile',
    redirect_uri: config.redirectUri,
    state,
    code_challenge: codeChallenge,
    code_challenge_method: 'S256',
  })
  return `https://${config.domain}/oauth2/authorize?${params}`
}

export function tokenUrl(config: CognitoConfig): string {
  return `https://${config.domain}/oauth2/token`
}

export function logoutUrl(config: CognitoConfig): string {
  const params = new URLSearchParams({
    client_id: config.clientId,
    logout_uri: config.logoutUri,
  })
  return `https://${config.domain}/logout?${params}`
}
