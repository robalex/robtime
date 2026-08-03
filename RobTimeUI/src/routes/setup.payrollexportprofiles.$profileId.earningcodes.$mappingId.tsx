import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeletePayrollEarningCodeMapping,
  usePayrollEarningCodeMappings,
  useUpdatePayrollEarningCodeMapping,
} from '@/features/payrollEarningCodeMappings/queries'
import {
  PayrollEarningCodeMappingForm,
  type PayrollEarningCodeMappingFormValues,
} from '@/features/payrollEarningCodeMappings/PayrollEarningCodeMappingForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId/earningcodes/$mappingId')({
  component: EditEarningCodeMapping,
})

function EditEarningCodeMapping() {
  const { profileId, mappingId } = Route.useParams()
  const profileIdNum = Number(profileId)
  const mappingIdNum = Number(mappingId)
  const navigate = useNavigate()

  // No single-mapping GET endpoint exists (same as PayRuleAssignment) — the edit route reads it out
  // of the already-fetched list for this profile.
  const { data: mappings, isPending, isError, error } = usePayrollEarningCodeMappings(profileIdNum)
  const updateMapping = useUpdatePayrollEarningCodeMapping(profileIdNum, mappingIdNum)
  const deleteMapping = useDeletePayrollEarningCodeMapping(profileIdNum)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const backToProfile = () =>
    navigate({
      to: '/setup/payrollexportprofiles/$profileId',
      params: { profileId },
      search: { tab: 'earningcodes' },
    })

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Could not load this mapping</h1>
        <p className="text-sm text-muted-foreground">
          {toApiProblem(error, 'Could not load earning-code mappings.').message}
        </p>
      </div>
    )
  }

  const mapping = mappings.find((m) => m.id === mappingIdNum)

  if (!mapping) {
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">Mapping not found</h1>
        <Link
          to="/setup/payrollexportprofiles/$profileId"
          params={{ profileId }}
          search={{ tab: 'earningcodes' }}
          className="text-sm underline underline-offset-4"
        >
          Back to earning codes
        </Link>
      </div>
    )
  }

  const defaultValues: PayrollEarningCodeMappingFormValues = {
    lineType: mapping.lineType,
    lineCode: mapping.lineCode,
    earningCode: mapping.earningCode,
    valueBasis: mapping.valueBasis,
    description: mapping.description,
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link
          to="/setup/payrollexportprofiles/$profileId"
          params={{ profileId }}
          search={{ tab: 'earningcodes' }}
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Earning codes
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{mapping.earningCode}</h1>
      </div>

      <PayrollEarningCodeMappingForm
        defaultValues={defaultValues}
        submitLabel="Save changes"
        onCancel={() => void backToProfile()}
        onSubmit={(values) =>
          updateMapping.mutateAsync({
            lineType: values.lineType,
            lineCode: values.lineCode,
            earningCode: values.earningCode,
            valueBasis: values.valueBasis,
            description: values.description || undefined,
          })
        }
      />

      <div className="max-w-2xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this mapping</h2>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteMapping.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteMapping.mutateAsync(mappingIdNum)
                  await backToProfile()
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this mapping.').message)
                }
              }}
            >
              {deleteMapping.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete mapping
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
