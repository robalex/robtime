import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useActivatePayRule, usePayRule } from '@/features/payRules/queries'
import { toApiProblem } from '@/lib/problem'
import { toWireLocalDate, todayLocalDate } from '@/lib/dates'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export const Route = createFileRoute('/setup/payrules/$payRuleId/activate')({
  component: ActivatePayRule,
})

// Its own route rather than a button that just fires the request — the effective date is the whole
// point (UI_PLAN.md §6 Rule 4's corollary), so it gets a form, not a one-click action.
function ActivatePayRule() {
  const { payRuleId } = Route.useParams()
  const id = Number(payRuleId)
  const navigate = useNavigate()

  const { data: rule, isPending, isError, error } = usePayRule(id)
  const activate = useActivatePayRule(id)
  const [effectiveFrom, setEffectiveFrom] = useState<string>(() => toWireLocalDate(todayLocalDate()))
  const [submitError, setSubmitError] = useState<string | null>(null)

  const backLink = (
    <Link
      to="/setup/payrules/$payRuleId"
      params={{ payRuleId }}
      className="text-sm text-muted-foreground underline-offset-4 hover:underline"
    >
      ← {rule?.name ?? 'Pay rule'}
    </Link>
  )

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError || !rule) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Could not load this pay rule</h1>
        <p className="text-sm text-muted-foreground">{toApiProblem(error).message}</p>
        <Link to="/setup/payrules" className="text-sm underline underline-offset-4">
          Back to pay rules
        </Link>
      </div>
    )
  }

  if (rule.status !== 'Draft') {
    return (
      <div className="space-y-4">
        {backLink}
        <h1 className="text-xl font-semibold">Only a Draft can be activated</h1>
        <p className="text-sm text-muted-foreground">
          {rule.name} is already {rule.status}.
        </p>
      </div>
    )
  }

  return (
    <div className="max-w-md space-y-6">
      <div className="space-y-1">
        {backLink}
        <h1 className="text-2xl font-semibold tracking-tight">Activate {rule.name}</h1>
        <p className="text-muted-foreground">
          Makes this the version employees are governed by. If another version of this rule is
          currently active, it's superseded as of the day before this date.
        </p>
      </div>

      <div className="space-y-2">
        <Label htmlFor="activate-effectiveFrom">Effective from</Label>
        <Input
          id="activate-effectiveFrom"
          type="date"
          value={effectiveFrom}
          onChange={(event) => setEffectiveFrom(event.target.value)}
        />
      </div>

      {submitError && <p className="text-sm text-destructive">{submitError}</p>}

      <div className="flex gap-2">
        <Button
          disabled={activate.isPending}
          onClick={async () => {
            setSubmitError(null)
            try {
              await activate.mutateAsync({ effectiveFrom })
              await navigate({ to: '/setup/payrules/$payRuleId', params: { payRuleId } })
            } catch (err) {
              setSubmitError(toApiProblem(err, 'Could not activate this pay rule.').message)
            }
          }}
        >
          {activate.isPending ? 'Activating…' : 'Activate'}
        </Button>
        <Button
          type="button"
          variant="outline"
          onClick={() => void navigate({ to: '/setup/payrules/$payRuleId', params: { payRuleId } })}
        >
          Cancel
        </Button>
      </div>
    </div>
  )
}
