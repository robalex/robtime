import { useRef, useState } from 'react'
import { createFileRoute, Link } from '@tanstack/react-router'
import { RequiresClient } from '@/components/RequiresClient'
import { Button, buttonVariants } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { toApiProblem } from '@/lib/problem'
import {
  useDeletePunchImportBatch,
  useImportPunches,
  usePunchImportBatches,
  type PunchImportBatch,
} from '@/features/punchImports/queries'

export const Route = createFileRoute('/people/punch-imports/')({
  component: PunchImportsIndex,
})

function PunchImportsIndex() {
  return <RequiresClient>{() => <PunchImportsPage />}</RequiresClient>
}

interface RowError {
  rowNumber: number
  field: string
  message: string
}

/** The backend reports validation failures with row-scoped keys like "row[12].PunchTime" — the same
 * `errors["row[i].field"] = messages` convention CreateBatchAsync already uses for its own per-row
 * errors — plus a plain "file" key for problems with the file as a whole (missing columns, empty
 * file). Split back into the two shapes here so the UI can render "which rows, and why" instead of a
 * flat, hard-to-scan list. */
function parseImportErrors(fieldErrors: Record<string, string>): { rowErrors: RowError[]; fileError: string | null } {
  const rowErrors: RowError[] = []
  let fileError: string | null = null

  for (const [key, message] of Object.entries(fieldErrors)) {
    const match = /^row\[(\d+)]\.(.+)$/.exec(key)
    if (match) {
      rowErrors.push({ rowNumber: Number(match[1]), field: match[2], message })
    } else if (key === 'file') {
      fileError = message
    }
  }

  rowErrors.sort((a, b) => a.rowNumber - b.rowNumber || a.field.localeCompare(b.field))
  return { rowErrors, fileError }
}

function PunchImportsPage() {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const [rowErrors, setRowErrors] = useState<RowError[]>([])
  const [fileError, setFileError] = useState<string | null>(null)
  const [generalError, setGeneralError] = useState<string | null>(null)
  const [successMessage, setSuccessMessage] = useState<string | null>(null)
  const importPunches = useImportPunches()

  async function handleImport() {
    if (!selectedFile) {
      return
    }
    setRowErrors([])
    setFileError(null)
    setGeneralError(null)
    setSuccessMessage(null)

    try {
      const batch = await importPunches.mutateAsync(selectedFile)
      setSuccessMessage(`Imported ${batch.punchCount} punch${batch.punchCount === 1 ? '' : 'es'} as batch #${batch.id}.`)
      setSelectedFile(null)
      if (fileInputRef.current) {
        fileInputRef.current.value = ''
      }
    } catch (err) {
      const problem = toApiProblem(err, 'Could not import this file.')
      const { rowErrors: parsedRows, fileError: parsedFile } = parseImportErrors(problem.fieldErrors)
      if (parsedRows.length > 0 || parsedFile) {
        setRowErrors(parsedRows)
        setFileError(parsedFile)
      } else {
        setGeneralError(problem.message)
      }
    }
  }

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link to="/people" search={{}} className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          ← People
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">Import punches</h1>
        <p className="text-muted-foreground">
          Bulk-load historical punches from a CSV file. Every row is checked before anything is
          saved — if any row has a problem, nothing in the file is imported.
        </p>
      </div>

      <Card>
        <CardHeader>
          <CardTitle className="text-base">Upload a file</CardTitle>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="space-y-1 text-sm text-muted-foreground">
            <p>
              Required columns: <code className="text-foreground">EmployeeId</code>,{' '}
              <code className="text-foreground">PunchTime</code> — the local wall-clock time where the
              punch happened, with no UTC offset or &quot;Z&quot; (e.g. 2026-06-01T13:00:00),{' '}
              <code className="text-foreground">Kind</code> (In / Out / FixedDollar / FixedHours).
            </p>
            <p>
              Optional columns: <code className="text-foreground">PunchTimeZoneId</code> — which time
              zone <code className="text-foreground">PunchTime</code> is in (e.g. America/New_York);
              defaults to the employee&apos;s own home time zone when left blank.{' '}
              <code className="text-foreground">DaylightSaving</code> — only needed when{' '}
              <code className="text-foreground">PunchTime</code> falls on the hour that repeats when
              clocks fall back for daylight saving time. Set it to <code className="text-foreground">true</code> for
              the earlier occurrence (still daylight saving time) or{' '}
              <code className="text-foreground">false</code> for the later one (already standard
              time); a punch time that falls in the hour skipped when clocks spring forward is always
              rejected, since it never actually happened. Also optional:{' '}
              <code className="text-foreground">Subtype</code>,{' '}
              <code className="text-foreground">PositionId</code>,{' '}
              <code className="text-foreground">Amount</code>, <code className="text-foreground">Hours</code>,{' '}
              <code className="text-foreground">BonusKind</code>,{' '}
              <code className="text-foreground">CountsTowardRegularRate</code>.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-3">
            <input
              ref={fileInputRef}
              type="file"
              accept=".csv,text/csv"
              onChange={(e) => {
                setSelectedFile(e.target.files?.[0] ?? null)
                setSuccessMessage(null)
              }}
              className="text-sm"
            />
            <Button
              size="sm"
              disabled={!selectedFile || importPunches.isPending}
              onClick={() => void handleImport()}
            >
              {importPunches.isPending ? 'Importing…' : 'Import'}
            </Button>
          </div>

          {successMessage && <p className="text-sm text-emerald-700 dark:text-emerald-400">{successMessage}</p>}
          {generalError && <p className="text-sm text-destructive">{generalError}</p>}
          {fileError && <p className="text-sm text-destructive">{fileError}</p>}

          {rowErrors.length > 0 && (
            <div className="space-y-2">
              <p className="text-sm font-medium text-destructive">
                {rowErrors.length} problem{rowErrors.length === 1 ? '' : 's'} found — nothing was imported.
              </p>
              <div className="max-h-96 overflow-y-auto overflow-x-auto rounded-lg border">
                <table className="w-full text-sm">
                  <thead className="border-b bg-muted/50 text-left">
                    <tr>
                      <th className="px-4 py-2 font-medium">Row</th>
                      <th className="px-4 py-2 font-medium">Field</th>
                      <th className="px-4 py-2 font-medium">Problem</th>
                    </tr>
                  </thead>
                  <tbody>
                    {rowErrors.map((rowError, i) => (
                      <tr key={i} className="border-b last:border-0">
                        <td className="px-4 py-2 tabular-nums">{rowError.rowNumber}</td>
                        <td className="px-4 py-2 text-muted-foreground">{rowError.field}</td>
                        <td className="px-4 py-2 text-destructive">{rowError.message}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}
        </CardContent>
      </Card>

      <PastImports />
    </div>
  )
}

function PastImports() {
  const { data, isPending, isError, error } = usePunchImportBatches()

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Past imports</CardTitle>
      </CardHeader>
      <CardContent>
        {isError && (
          <p className="text-sm text-destructive">{toApiProblem(error, 'Could not load past imports.').message}</p>
        )}
        {isPending ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : data && data.items.length > 0 ? (
          <div className="overflow-x-auto rounded-lg border">
            <table className="w-full text-sm">
              <thead className="border-b bg-muted/50 text-left">
                <tr>
                  <th className="px-4 py-2 font-medium">File</th>
                  <th className="px-4 py-2 font-medium">Punches</th>
                  <th className="px-4 py-2 font-medium">Imported</th>
                  <th className="px-4 py-2 font-medium">Status</th>
                  <th className="px-4 py-2" />
                </tr>
              </thead>
              <tbody>
                {data.items.map((batch) => (
                  <BatchRow key={batch.id} batch={batch} />
                ))}
              </tbody>
            </table>
          </div>
        ) : (
          <p className="text-sm text-muted-foreground">No imports yet.</p>
        )}
      </CardContent>
    </Card>
  )
}

function BatchRow({ batch }: { batch: PunchImportBatch }) {
  const [confirming, setConfirming] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const deleteBatch = useDeletePunchImportBatch()

  async function handleDelete() {
    setDeleteError(null)
    try {
      await deleteBatch.mutateAsync(batch.id)
      setConfirming(false)
    } catch (err) {
      setDeleteError(toApiProblem(err, 'Could not delete this batch.').message)
    }
  }

  return (
    <tr className="border-b last:border-0">
      <td className="px-4 py-2">
        {batch.fileName} <span className="text-xs text-muted-foreground">#{batch.id}</span>
      </td>
      <td className="px-4 py-2 tabular-nums">{batch.punchCount}</td>
      <td className="px-4 py-2 text-muted-foreground">
        {formatInstant(batch.importedAt)} by {batch.importedByUserId}
      </td>
      <td className="px-4 py-2">
        {batch.deletedAt ? (
          <span className="text-xs text-muted-foreground">
            Deleted {formatInstant(batch.deletedAt)} by {batch.deletedByUserId}
          </span>
        ) : (
          <span className="text-xs text-emerald-700 dark:text-emerald-400">Active</span>
        )}
      </td>
      <td className="px-4 py-2 text-right">
        {!batch.deletedAt &&
          (confirming ? (
            <div className="flex items-center justify-end gap-2">
              <span className="text-xs text-muted-foreground">Permanently delete {batch.punchCount} punches?</span>
              <Button size="sm" variant="destructive" disabled={deleteBatch.isPending} onClick={() => void handleDelete()}>
                {deleteBatch.isPending ? 'Deleting…' : 'Yes, delete'}
              </Button>
              <Button size="sm" variant="outline" onClick={() => setConfirming(false)}>
                Cancel
              </Button>
            </div>
          ) : (
            <Button size="sm" variant="outline" onClick={() => setConfirming(true)}>
              Delete batch
            </Button>
          ))}
        {deleteError && <p className="mt-1 text-xs text-destructive">{deleteError}</p>}
      </td>
    </tr>
  )
}

function formatInstant(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: 'numeric',
    minute: '2-digit',
  })
}

// Referenced only to keep the "New employee" style link affordance consistent if this page's header
// ever grows a second action — kept for parity with other People sub-pages' buttonVariants usage.
void buttonVariants
