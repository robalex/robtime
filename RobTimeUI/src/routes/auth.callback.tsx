import { useEffect, useRef, useState } from 'react'
import { createFileRoute, useRouter } from '@tanstack/react-router'
import { useAuth } from '@/auth/AuthProvider'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/auth/callback')({
  // Cognito appends ?code=…&state=… (or ?error=… when the user cancels or the client is
  // misconfigured). Typed here so the component reads them without touching raw URLSearchParams.
  validateSearch: (search: Record<string, unknown>) => ({
    code: typeof search.code === 'string' ? search.code : undefined,
    state: typeof search.state === 'string' ? search.state : undefined,
    error: typeof search.error === 'string' ? search.error : undefined,
    error_description:
      typeof search.error_description === 'string' ? search.error_description : undefined,
  }),
  component: AuthCallback,
})

function AuthCallback() {
  const { code, state, error, error_description } = Route.useSearch()
  const { completeSignIn } = useAuth()
  const router = useRouter()
  const [failure, setFailure] = useState<string | null>(
    error ? (error_description ?? error) : null,
  )

  // The authorization code is single-use — redeeming it twice fails. StrictMode double-invokes
  // effects in development, so this guard is load-bearing, not defensive noise.
  const exchangeStarted = useRef(false)

  useEffect(() => {
    if (error || !code || !state || exchangeStarted.current) {
      return
    }
    exchangeStarted.current = true

    completeSignIn(code, state)
      // `href`, not `to`: returnTo is a fully-built path+search+hash string (location.href), and
      // `to` is meant for typed route paths — it won't reparse an embedded "?tab=punches" back into
      // search params the way `href` does.
      .then((returnTo) => router.navigate({ href: returnTo, replace: true }))
      .catch((err: unknown) => setFailure(err instanceof Error ? err.message : 'Sign-in failed.'))
  }, [code, state, error, completeSignIn, router])

  if (failure) {
    return (
      <div className="mx-auto max-w-md space-y-4 py-16 text-center">
        <h1 className="text-xl font-semibold">Sign-in failed</h1>
        <p className="text-sm text-muted-foreground">{failure}</p>
        <Button onClick={() => router.navigate({ to: '/login', replace: true })}>Try again</Button>
      </div>
    )
  }

  return <p className="py-16 text-center text-muted-foreground">Signing you in…</p>
}
