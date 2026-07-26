import { createFileRoute, Link } from '@tanstack/react-router'
import { usePayRules } from '@/features/payRules/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/payrules/')({
  component: PayRulesIndex,
})

function PayRulesIndex() {
  return <RequiresClient>{(clientId) => <PayRuleList clientId={clientId} />}</RequiresClient>
}

function PayRuleList({ clientId }: { clientId: number }) {
  // Pay rules are a small, bounded set per client (one per jurisdiction, plus their version
  // history) — a single page of 100 is plenty, same reasoning as the Positions list.
  const { data, isPending, isError, error } = usePayRules({ clientId, page: 1, pageSize: 100 })

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Pay rules</h1>
          <p className="text-muted-foreground">
            Overtime, rounding, and premium configuration. Employees are assigned to these over time.
          </p>
        </div>
        <Link to="/setup/payrules/new" className={buttonVariants()}>
          New pay rule
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load pay rules.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Name</th>
                <th className="px-4 py-2 font-medium">Template</th>
                <th className="px-4 py-2 font-medium">Version</th>
                <th className="px-4 py-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((payRule) => (
                <tr key={payRule.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/payrules/$payRuleId"
                      params={{ payRuleId: String(payRule.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {payRule.name}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{payRule.templateCode ?? '—'}</td>
                  <td className="px-4 py-2 text-muted-foreground tabular-nums">{payRule.version}</td>
                  <td className="px-4 py-2 text-muted-foreground">{payRule.status}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          No pay rules yet. Create one from a jurisdiction template to get started.
        </p>
      )}
    </div>
  )
}
