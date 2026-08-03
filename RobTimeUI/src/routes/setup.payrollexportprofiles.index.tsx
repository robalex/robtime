import { createFileRoute, Link } from '@tanstack/react-router'
import { usePayrollExportProfiles } from '@/features/payrollExportProfiles/queries'
import { PROVIDER_LABELS } from '@/features/payrollExportProfiles/formSchema'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/payrollexportprofiles/')({
  component: PayrollExportProfilesIndex,
})

function PayrollExportProfilesIndex() {
  return <RequiresClient>{(clientId) => <PayrollExportProfileList clientId={clientId} />}</RequiresClient>
}

function PayrollExportProfileList({ clientId }: { clientId: number }) {
  // A small, bounded set per client (a handful of provider profiles) — a single page of 100 is plenty.
  const { data, isPending, isError, error } = usePayrollExportProfiles({ clientId, page: 1, pageSize: 100 })

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Payroll export</h1>
          <p className="text-muted-foreground">
            Earning-code mappings, employee identifiers, and export runs, per payroll provider.
          </p>
        </div>
        <Link to="/setup/payrollexportprofiles/new" className={buttonVariants()}>
          New profile
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load payroll export profiles.').message}
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
                <th className="px-4 py-2 font-medium">Provider</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((profile) => (
                <tr key={profile.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/payrollexportprofiles/$profileId"
                      params={{ profileId: String(profile.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {profile.name}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{PROVIDER_LABELS[profile.provider]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No payroll export profiles yet.</p>
      )}
    </div>
  )
}
