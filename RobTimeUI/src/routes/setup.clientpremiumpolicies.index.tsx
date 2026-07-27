import { createFileRoute, Link } from '@tanstack/react-router'
import { useClientPremiumPolicies } from '@/features/clientPremiumPolicies/queries'
import { usePremiumRules } from '@/features/premiumRules/queries'
import { WAIVER_POLICY_LABELS } from '@/features/clientPremiumPolicies/formSchema'
import { formatLocalDate, parseLocalDate } from '@/lib/dates'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/clientpremiumpolicies/')({
  component: ClientPremiumPoliciesIndex,
})

function ClientPremiumPoliciesIndex() {
  return <RequiresClient>{(clientId) => <ClientPremiumPolicyList clientId={clientId} />}</RequiresClient>
}

function ClientPremiumPolicyList({ clientId }: { clientId: number }) {
  // Waiver policies are a small, bounded set per client (one row per premium code, occasionally
  // superseded over time) — a single page of 100 is plenty, same reasoning as Differential rules.
  const { data, isPending, isError, error } = useClientPremiumPolicies({ clientId, page: 1, pageSize: 100 })
  const { data: premiumRules } = usePremiumRules()

  const nameFor = (code: string) => premiumRules?.find((rule) => rule.code === code)?.name ?? code

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Waiver policies</h1>
          <p className="text-muted-foreground">
            This client's own determination of whether each premium can be waived, overriding that
            premium's built-in default.
          </p>
        </div>
        <Link to="/setup/clientpremiumpolicies/new" className={buttonVariants()}>
          New waiver policy
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load waiver policies.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Premium</th>
                <th className="px-4 py-2 font-medium">Waiver policy</th>
                <th className="px-4 py-2 font-medium">Effective</th>
                <th className="px-4 py-2 font-medium">Set by</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((policy) => (
                <tr key={policy.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/clientpremiumpolicies/$clientPremiumPolicyId"
                      params={{ clientPremiumPolicyId: String(policy.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {nameFor(policy.premiumCode)}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{WAIVER_POLICY_LABELS[policy.waiverPolicy]}</td>
                  <td className="px-4 py-2 text-muted-foreground">
                    {formatLocalDate(parseLocalDate(policy.effectiveFrom))}
                    {policy.effectiveTo ? ` – ${formatLocalDate(parseLocalDate(policy.effectiveTo))}` : ' – present'}
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{policy.setBy}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          No waiver policies set. Without one, each premium falls back to its own built-in default.
        </p>
      )}
    </div>
  )
}
