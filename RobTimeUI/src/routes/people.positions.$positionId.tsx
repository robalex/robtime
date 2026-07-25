import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useDeletePosition, usePosition, useUpdatePosition } from '@/features/positions/queries'
import { PositionForm } from '@/features/positions/PositionForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/people/positions/$positionId')({
  component: EditPosition,
})

function EditPosition() {
  const { positionId } = Route.useParams()
  const id = Number(positionId)
  const navigate = useNavigate()

  const { data: position, isPending, isError, error } = usePosition(id)
  const updatePosition = useUpdatePosition(id)
  const deletePosition = useDeletePosition()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this position.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Position not found' : 'Could not load this position'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/people/positions" className="text-sm underline underline-offset-4">
          Back to positions
        </Link>
      </div>
    )
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link to="/people/positions" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          ← Positions
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">
          {position.code} — {position.name}
        </h1>
      </div>

      <PositionForm
        defaultValues={{ code: position.code, name: position.name, baseRate: position.baseRate }}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/people/positions' })}
        onSubmit={(values) => updatePosition.mutateAsync(values)}
      />

      <div className="space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this position</h2>
          <p className="text-sm text-muted-foreground">
            Soft-deleted. Existing assignments and historical pay that reference it are retained.
          </p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deletePosition.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deletePosition.mutateAsync(id)
                  await navigate({ to: '/people/positions' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this position.').message)
                }
              }}
            >
              {deletePosition.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete position
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
