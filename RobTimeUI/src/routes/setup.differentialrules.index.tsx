import { createFileRoute, Link } from '@tanstack/react-router'
import { useDifferentialRules } from '@/features/differentialRules/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/differentialrules/')({
  component: DifferentialRulesIndex,
})

function DifferentialRulesIndex() {
  return <RequiresClient>{(clientId) => <DifferentialRuleList clientId={clientId} />}</RequiresClient>
}

function DifferentialRuleList({ clientId }: { clientId: number }) {
  // Differential rules are a small, bounded set per client (a handful of named pay differentials) —
  // a single page of 100 is plenty, same reasoning as Positions and Pay rules.
  const { data, isPending, isError, error } = useDifferentialRules({ clientId, page: 1, pageSize: 100 })

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Differential rules</h1>
          <p className="text-muted-foreground">
            Time-based pay differentials — night shift, weekend, holiday. Assign a rule's code to a
            pay rule to turn it on for that jurisdiction.
          </p>
        </div>
        <Link to="/setup/differentialrules/new" className={buttonVariants()}>
          New differential rule
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load differential rules.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Code</th>
                <th className="px-4 py-2 font-medium">Active on</th>
                <th className="px-4 py-2 font-medium">Adjustment</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((rule) => (
                <tr key={rule.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/differentialrules/$differentialRuleId"
                      params={{ differentialRuleId: String(rule.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {rule.code}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{rule.dayScheduleMode}</td>
                  <td className="px-4 py-2 text-muted-foreground">
                    {rule.adjustmentType === 'Multiplier'
                      ? `+${(rule.adjustmentValue * 100).toFixed(0)}%`
                      : `$${rule.adjustmentValue.toFixed(2)}`}{' '}
                    {rule.adjustmentType === 'FixedBonus' ? 'bonus' : 'per hour'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          No differential rules yet. Create one, then add its code to a pay rule's active
          differentials.
        </p>
      )}
    </div>
  )
}
