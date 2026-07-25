import { createFileRoute, Link } from '@tanstack/react-router'
import { usePositions } from '@/features/positions/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/people/positions/')({
  component: PositionsIndex,
})

function PositionsIndex() {
  return <RequiresClient>{(clientId) => <PositionList clientId={clientId} />}</RequiresClient>
}

function PositionList({ clientId }: { clientId: number }) {
  // Positions are a small, bounded set per client (job codes, not people) — a single page of 100 is
  // plenty and avoids paging UI that would almost never be used.
  const { data, isPending, isError, error } = usePositions({ clientId, page: 1, pageSize: 100 })

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/people" search={{}} className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← People
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Positions</h1>
          <p className="text-muted-foreground">
            Job codes and their default rates. Employees are assigned to these over time.
          </p>
        </div>
        <Link to="/people/positions/new" className={buttonVariants()}>
          New position
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load positions.').message}
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
                <th className="px-4 py-2 font-medium">Name</th>
                <th className="px-4 py-2 font-medium">Base rate</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((position) => (
                <tr key={position.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/people/positions/$positionId"
                      params={{ positionId: String(position.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {position.code}
                    </Link>
                  </td>
                  <td className="px-4 py-2">{position.name}</td>
                  <td className="px-4 py-2 tabular-nums text-muted-foreground">
                    {position.baseRate.toFixed(2)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          No positions yet. Create one before assigning employees to it.
        </p>
      )}
    </div>
  )
}
