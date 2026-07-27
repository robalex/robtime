import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeleteStateMinimumWage,
  useStateMinimumWage,
  useUpdateStateMinimumWage,
} from '@/features/stateMinimumWages/queries'
import {
  StateMinimumWageForm,
} from '@/features/stateMinimumWages/StateMinimumWageForm'
import type { StateMinimumWageFormValues } from '@/features/stateMinimumWages/formSchema'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/stateminimumwages/$stateMinimumWageId')({
  component: EditStateMinimumWage,
})

function EditStateMinimumWage() {
  const { stateMinimumWageId } = Route.useParams()
  const id = Number(stateMinimumWageId)
  const navigate = useNavigate()

  const { data: wage, isPending, isError, error } = useStateMinimumWage(id)
  const updateStateMinimumWage = useUpdateStateMinimumWage(id)
  const deleteStateMinimumWage = useDeleteStateMinimumWage()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this rate.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Rate not found' : 'Could not load this rate'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/setup/stateminimumwages" className="text-sm underline underline-offset-4">
          Back to state minimum wages
        </Link>
      </div>
    )
  }

  const defaultValues: StateMinimumWageFormValues = {
    state: wage.state,
    effectiveFrom: wage.effectiveFrom,
    effectiveTo: wage.effectiveTo ?? '',
    amount: wage.amount,
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link
          to="/setup/stateminimumwages"
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← State minimum wages
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{wage.state}</h1>
      </div>

      <StateMinimumWageForm
        defaultValues={defaultValues}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/stateminimumwages' })}
        onSubmit={(values) =>
          updateStateMinimumWage.mutateAsync({
            state: values.state,
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
            amount: values.amount,
          })
        }
      />

      <div className="max-w-md space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this rate</h2>
          <p className="text-sm text-muted-foreground">Permanently removed — this is a fact about a date range, not a record with its own history.</p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteStateMinimumWage.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteStateMinimumWage.mutateAsync(id)
                  await navigate({ to: '/setup/stateminimumwages' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this rate.').message)
                }
              }}
            >
              {deleteStateMinimumWage.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete rate
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
