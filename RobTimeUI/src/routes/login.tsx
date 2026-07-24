import { useState } from 'react'
import { createFileRoute } from '@tanstack/react-router'
import { useAuth } from '@/auth/AuthProvider'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/login')({
  // Returns an object with `redirect` *absent* rather than present-and-undefined, so navigating to
  // /login without a redirect doesn't have to pass an explicit empty search object.
  validateSearch: (search: Record<string, unknown>): { redirect?: string } =>
    typeof search.redirect === 'string' ? { redirect: search.redirect } : {},
  component: Login,
})

function Login() {
  const { signIn } = useAuth()
  const { redirect } = Route.useSearch()
  const [error, setError] = useState<string | null>(null)

  // No password field here by design: credentials are entered on Cognito's managed login page, so
  // this app never sees or handles them.
  const handleSignIn = () => {
    signIn(redirect ?? '/').catch((err: unknown) =>
      setError(err instanceof Error ? err.message : 'Could not start sign-in.'),
    )
  }

  return (
    <div className="mx-auto max-w-sm space-y-6 py-24 text-center">
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">RobTime</h1>
        <p className="text-sm text-muted-foreground">Sign in to continue.</p>
      </div>
      <Button className="w-full" onClick={handleSignIn}>
        Sign in
      </Button>
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  )
}
