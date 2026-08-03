import { useState, type FormEvent } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeletePayrollExportProfile,
  usePayrollExportProfile,
  useUpdatePayrollExportProfile,
} from '@/features/payrollExportProfiles/queries'
import {
  PayrollExportProfileForm,
  type PayrollExportProfileFormValues,
} from '@/features/payrollExportProfiles/PayrollExportProfileForm'
import { usePayrollEarningCodeMappings } from '@/features/payrollEarningCodeMappings/queries'
import { LINE_TYPE_LABELS, VALUE_BASIS_LABELS } from '@/features/payrollEarningCodeMappings/formSchema'
import { usePayrollEmployeeIdentifiers } from '@/features/payrollEmployeeIdentifiers/queries'
import { useEmployees } from '@/features/employees/queries'
import {
  useCreatePayrollExportBatch,
  useDownloadPayrollExportBatch,
  usePayrollExportBatches,
  useVoidPayrollExportBatch,
  type PayrollExportBatch,
} from '@/features/payrollExportBatches/queries'
import { toApiProblem } from '@/lib/problem'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'

const TABS = [
  { id: 'details', label: 'Details' },
  { id: 'earningcodes', label: 'Earning codes' },
  { id: 'identifiers', label: 'Employee identifiers' },
  { id: 'exports', label: 'Exports' },
] as const

type TabId = (typeof TABS)[number]['id']

export const Route = createFileRoute('/setup/payrollexportprofiles/$profileId/')({
  // The active tab is URL state, so a specific tab is linkable and survives a refresh — same
  // convention as people.$employeeId.index.tsx.
  validateSearch: (search: Record<string, unknown>): { tab?: TabId } => {
    const tab = TABS.find((t) => t.id === search.tab)?.id
    return tab && tab !== 'details' ? { tab } : {}
  },
  component: PayrollExportProfileDetail,
})

function PayrollExportProfileDetail() {
  const { profileId } = Route.useParams()
  const { tab } = Route.useSearch()
  const activeTab: TabId = tab ?? 'details'
  const id = Number(profileId)
  const navigate = useNavigate()

  const { data: profile, isPending, isError, error } = usePayrollExportProfile(id)
  const updateProfile = useUpdatePayrollExportProfile(id)
  const deleteProfile = useDeletePayrollExportProfile()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this payroll export profile.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Payroll export profile not found' : 'Could not load this payroll export profile'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/setup/payrollexportprofiles" className="text-sm underline underline-offset-4">
          Back to payroll export
        </Link>
      </div>
    )
  }

  const defaultValues: PayrollExportProfileFormValues = {
    name: profile.name,
    provider: profile.provider,
    grouping: profile.grouping,
    roundingPolicy: profile.roundingPolicy,
    adjustmentEarningCode: profile.adjustmentEarningCode,
    amountScale: profile.amountScale,
    hoursScale: profile.hoursScale,
  }

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link
          to="/setup/payrollexportprofiles"
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Payroll export
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{profile.name}</h1>
      </div>

      <div className="border-b">
        <nav className="-mb-px flex gap-4">
          {TABS.map((t) => (
            <button
              key={t.id}
              type="button"
              onClick={() =>
                void navigate({
                  to: '/setup/payrollexportprofiles/$profileId',
                  params: { profileId },
                  search: t.id === 'details' ? {} : { tab: t.id },
                })
              }
              className={cn(
                'border-b-2 px-1 py-2 text-sm font-medium transition-colors',
                activeTab === t.id
                  ? 'border-foreground text-foreground'
                  : 'border-transparent text-muted-foreground hover:text-foreground',
              )}
            >
              {t.label}
            </button>
          ))}
        </nav>
      </div>

      {activeTab === 'details' && (
        <div className="space-y-8">
          <PayrollExportProfileForm
            defaultValues={defaultValues}
            submitLabel="Save changes"
            onCancel={() => void navigate({ to: '/setup/payrollexportprofiles' })}
            onSubmit={(values) =>
              updateProfile.mutateAsync({
                name: values.name,
                provider: values.provider,
                grouping: values.grouping,
                roundingPolicy: values.roundingPolicy,
                adjustmentEarningCode: values.adjustmentEarningCode || undefined,
                amountScale: values.amountScale,
                hoursScale: values.hoursScale,
              })
            }
          />

          <div className="max-w-2xl space-y-3 border-t pt-6">
            <div className="space-y-1">
              <h2 className="text-sm font-medium">Delete this profile</h2>
              <p className="text-sm text-muted-foreground">
                Blocked while any earning-code mapping is still attached.
              </p>
            </div>
            {confirmingDelete ? (
              <div className="flex items-center gap-2">
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={deleteProfile.isPending}
                  onClick={async () => {
                    setDeleteError(null)
                    try {
                      await deleteProfile.mutateAsync(id)
                      await navigate({ to: '/setup/payrollexportprofiles' })
                    } catch (err) {
                      setDeleteError(toApiProblem(err, 'Could not delete this profile.').message)
                    }
                  }}
                >
                  {deleteProfile.isPending ? 'Deleting…' : 'Yes, delete'}
                </Button>
                <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
                  Keep it
                </Button>
              </div>
            ) : (
              <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
                Delete profile
              </Button>
            )}
            {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
          </div>
        </div>
      )}

      {activeTab === 'earningcodes' && <EarningCodesTab profileId={id} profileIdParam={profileId} />}
      {activeTab === 'identifiers' && (
        <IdentifiersTab profileId={id} profileIdParam={profileId} clientId={profile.clientId} />
      )}
      {activeTab === 'exports' && <ExportsTab profileId={id} profileIdParam={profileId} />}
    </div>
  )
}

function EarningCodesTab({ profileId, profileIdParam }: { profileId: number; profileIdParam: string }) {
  const { data: mappings, isPending, isError, error } = usePayrollEarningCodeMappings(profileId)

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Which earning code payroll should use for each pay line type this profile exports.
        </p>
        <Link
          to="/setup/payrollexportprofiles/$profileId/earningcodes/new"
          params={{ profileId: profileIdParam }}
          className={buttonVariants({ size: 'sm' })}
        >
          Add mapping
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load earning-code mappings.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : mappings && mappings.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Line type</th>
                <th className="px-4 py-2 font-medium">Line code</th>
                <th className="px-4 py-2 font-medium">Earning code</th>
                <th className="px-4 py-2 font-medium">Value basis</th>
              </tr>
            </thead>
            <tbody>
              {mappings.map((mapping) => (
                <tr key={mapping.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/payrollexportprofiles/$profileId/earningcodes/$mappingId"
                      params={{ profileId: profileIdParam, mappingId: String(mapping.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {LINE_TYPE_LABELS[mapping.lineType]}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{mapping.lineCode || '—'}</td>
                  <td className="px-4 py-2 text-muted-foreground">{mapping.earningCode}</td>
                  <td className="px-4 py-2 text-muted-foreground">{VALUE_BASIS_LABELS[mapping.valueBasis]}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No earning-code mappings yet.</p>
      )}
    </div>
  )
}

function IdentifiersTab({
  profileId,
  profileIdParam,
  clientId,
}: {
  profileId: number
  profileIdParam: string
  clientId: number
}) {
  const { data: identifiers, isPending, isError, error } = usePayrollEmployeeIdentifiers(profileId)
  const { data: employeePage } = useEmployees({ clientId, page: 1, pageSize: 200 })
  const employeeName = (employeeId: number) => {
    const employee = employeePage?.items.find((e) => e.id === employeeId)
    return employee ? `${employee.firstName} ${employee.lastName}` : `Employee #${employeeId}`
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          Which external id this provider uses for each employee.
        </p>
        <Link
          to="/setup/payrollexportprofiles/$profileId/identifiers/new"
          params={{ profileId: profileIdParam }}
          className={buttonVariants({ size: 'sm' })}
        >
          Add identifier
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load employee identifiers.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : identifiers && identifiers.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Employee</th>
                <th className="px-4 py-2 font-medium">External employee id</th>
              </tr>
            </thead>
            <tbody>
              {identifiers.map((identifier) => (
                <tr key={identifier.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/payrollexportprofiles/$profileId/identifiers/$identifierId"
                      params={{ profileId: profileIdParam, identifierId: String(identifier.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {employeeName(identifier.employeeId)}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{identifier.externalEmployeeId}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No employee identifiers yet.</p>
      )}
    </div>
  )
}

function ExportsTab({ profileId }: { profileId: number; profileIdParam: string }) {
  const { data: batches, isPending, isError, error } = usePayrollExportBatches(profileId)

  return (
    <div className="space-y-8">
      <TriggerExportForm profileId={profileId} />

      {isError && (
        <p className="text-sm text-destructive">{toApiProblem(error, 'Could not load past exports.').message}</p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : batches && batches.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Period</th>
                <th className="px-4 py-2 font-medium">Employees</th>
                <th className="px-4 py-2 font-medium">Rows</th>
                <th className="px-4 py-2 font-medium">Total</th>
                <th className="px-4 py-2 font-medium">Exported</th>
                <th className="px-4 py-2 font-medium" />
              </tr>
            </thead>
            <tbody>
              {batches.items.map((batch) => (
                <ExportBatchRow key={batch.id} profileId={profileId} batch={batch} />
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No exports yet.</p>
      )}
    </div>
  )
}

function TriggerExportForm({ profileId }: { profileId: number }) {
  const [periodStart, setPeriodStart] = useState('')
  const [periodEnd, setPeriodEnd] = useState('')
  const [triggerError, setTriggerError] = useState<string | null>(null)
  const createExport = useCreatePayrollExportBatch(profileId)

  const submit = async (event: FormEvent) => {
    event.preventDefault()
    setTriggerError(null)
    if (periodEnd < periodStart) {
      setTriggerError('The end date cannot be before the start date.')
      return
    }
    try {
      await createExport.mutateAsync({ periodStart, periodEnd })
      setPeriodStart('')
      setPeriodEnd('')
    } catch (err) {
      setTriggerError(toApiProblem(err, 'Could not create this export.').message)
    }
  }

  return (
    <form onSubmit={(event) => void submit(event)} className="space-y-3 rounded-lg border p-4">
      <h2 className="text-sm font-medium">Trigger an export</h2>
      <p className="text-sm text-muted-foreground">
        Exports every approved timecard whose period exactly matches the dates below.
      </p>
      <div className="flex flex-wrap items-end gap-4">
        <div className="space-y-2">
          <Label htmlFor="export-periodStart">Period start</Label>
          <Input
            id="export-periodStart"
            type="date"
            required
            value={periodStart}
            onChange={(event) => setPeriodStart(event.target.value)}
          />
        </div>
        <div className="space-y-2">
          <Label htmlFor="export-periodEnd">Period end</Label>
          <Input
            id="export-periodEnd"
            type="date"
            required
            value={periodEnd}
            onChange={(event) => setPeriodEnd(event.target.value)}
          />
        </div>
        <Button type="submit" disabled={createExport.isPending}>
          {createExport.isPending ? 'Exporting…' : 'Export'}
        </Button>
      </div>
      {triggerError && <p className="text-sm text-destructive">{triggerError}</p>}
    </form>
  )
}

function ExportBatchRow({ profileId, batch }: { profileId: number; batch: PayrollExportBatch }) {
  const downloadBatch = useDownloadPayrollExportBatch(profileId)
  const voidBatch = useVoidPayrollExportBatch(profileId)
  const [downloadError, setDownloadError] = useState<string | null>(null)
  const [voidError, setVoidError] = useState<string | null>(null)
  const [confirmingVoid, setConfirmingVoid] = useState(false)

  return (
    <tr className="border-b last:border-0 hover:bg-muted/40 align-top">
      <td className="px-4 py-2">
        {formatLocalDateString(batch.periodStart)} – {formatLocalDateString(batch.periodEnd)}
      </td>
      <td className="px-4 py-2 text-muted-foreground">{batch.employeeCount}</td>
      <td className="px-4 py-2 text-muted-foreground">{batch.rowCount}</td>
      <td className="px-4 py-2 text-muted-foreground">${batch.totalAmount.toFixed(2)}</td>
      <td className="px-4 py-2 text-muted-foreground">
        {formatInstant(batch.exportedAt)}
        {batch.voidedAt && (
          <div className="text-xs text-destructive">Voided {formatInstant(batch.voidedAt)}</div>
        )}
      </td>
      <td className="px-4 py-2">
        <div className="flex flex-col items-end gap-1">
          <div className="flex items-center gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={downloadBatch.isPending}
              onClick={async () => {
                setDownloadError(null)
                try {
                  await downloadBatch.mutateAsync({ id: batch.id, fileName: batch.fileName })
                } catch (err) {
                  setDownloadError(toApiProblem(err, 'Could not download this export.').message)
                }
              }}
            >
              {downloadBatch.isPending ? 'Downloading…' : 'Download'}
            </Button>
            {!batch.voidedAt &&
              (confirmingVoid ? (
                <>
                  <Button
                    size="sm"
                    variant="destructive"
                    disabled={voidBatch.isPending}
                    onClick={async () => {
                      setVoidError(null)
                      try {
                        await voidBatch.mutateAsync(batch.id)
                        setConfirmingVoid(false)
                      } catch (err) {
                        setVoidError(toApiProblem(err, 'Could not void this export.').message)
                      }
                    }}
                  >
                    {voidBatch.isPending ? 'Voiding…' : 'Yes, void'}
                  </Button>
                  <Button size="sm" variant="outline" onClick={() => setConfirmingVoid(false)}>
                    Cancel
                  </Button>
                </>
              ) : (
                <Button size="sm" variant="outline" onClick={() => setConfirmingVoid(true)}>
                  Void
                </Button>
              ))}
          </div>
          {downloadError && <p className="text-xs text-destructive">{downloadError}</p>}
          {voidError && <p className="text-xs text-destructive">{voidError}</p>}
        </div>
      </td>
    </tr>
  )
}

function formatLocalDateString(iso: string): string {
  return new Date(`${iso}T00:00:00`).toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' })
}

function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit' })
}
