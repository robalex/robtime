import { useRef } from 'react'
import { Plus, TriangleAlert, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select } from '@/components/ui/select'
import { blankTestPunchRow, type TestPunchRow } from './testPunchRow'

interface TestPunchEntryProps {
  rows: TestPunchRow[]
  onChange: (rows: TestPunchRow[]) => void
  /** Row keys the last Run flagged as an ambiguous fall-back hour. */
  ambiguousKeys: Set<string>
  /** Row keys with any other resolve error from the last Run, keyed to the message. */
  rowErrors: Map<string, string>
}

/**
 * Keyboard-first test-punch entry, deliberately minimal compared to BulkPunchEntry — differentials
 * only ever look at In/Out pairs (DifferentialApplier iterates PunchPairs, never FixedEntries), so
 * there's no amount/hours/position to collect. Sandbox-local state only; never saved as real punches
 * — resolution and DST handling happen server-side in the same Run call that evaluates them.
 */
export function TestPunchEntry({ rows, onChange, ambiguousKeys, rowErrors }: TestPunchEntryProps) {
  const rowCounter = useRef(rows.length)

  function updateRow(key: string, patch: Partial<TestPunchRow>) {
    onChange(rows.map((row) => (row.key === key ? { ...row, ...patch } : row)))
  }

  function removeRow(key: string) {
    onChange(rows.length > 1 ? rows.filter((row) => row.key !== key) : rows)
  }

  function addRow() {
    const last = rows[rows.length - 1]
    const next = blankTestPunchRow(rowCounter, last?.when ?? '', last?.kind === 'In' ? 'Out' : 'In')
    onChange([...rows, next])
  }

  return (
    <div className="space-y-2">
      <div className="overflow-x-auto rounded-lg border">
        <table className="w-full text-sm">
          <thead className="border-b bg-muted/50 text-left">
            <tr>
              <th className="px-3 py-2 font-medium">When (local)</th>
              <th className="px-3 py-2 font-medium">Kind</th>
              <th className="px-3 py-2 font-medium">Zone override</th>
              <th className="px-3 py-2" />
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.key} className="border-b last:border-0">
                <td className="px-3 py-1.5">
                  <Input
                    type="datetime-local"
                    className="h-8 min-w-[190px]"
                    value={row.when}
                    onChange={(e) => updateRow(row.key, { when: e.target.value, daylightSaving: undefined })}
                  />
                  {ambiguousKeys.has(row.key) && (
                    <div className="mt-1 flex items-center gap-1">
                      <TriangleAlert className="size-3.5 shrink-0 text-amber-600 dark:text-amber-500" />
                      <Select
                        className="h-6 text-xs"
                        value={row.daylightSaving === undefined ? '' : String(row.daylightSaving)}
                        onChange={(e) =>
                          updateRow(row.key, {
                            daylightSaving: e.target.value === '' ? undefined : e.target.value === 'true',
                          })
                        }
                      >
                        <option value="">This time happens twice — which one?</option>
                        <option value="true">Earlier (still daylight saving time)</option>
                        <option value="false">Later (already standard time)</option>
                      </Select>
                    </div>
                  )}
                  {!ambiguousKeys.has(row.key) && rowErrors.get(row.key) && (
                    <p className="mt-1 text-xs text-destructive">{rowErrors.get(row.key)}</p>
                  )}
                </td>
                <td className="px-3 py-1.5">
                  <Select
                    className="h-8 w-28"
                    value={row.kind}
                    onChange={(e) => updateRow(row.key, { kind: e.target.value as 'In' | 'Out' })}
                  >
                    <option value="In">Clock In</option>
                    <option value="Out">Clock Out</option>
                  </Select>
                </td>
                <td className="px-3 py-1.5">
                  <Input
                    className="h-8 w-44"
                    placeholder="Employee's own zone"
                    value={row.timeZoneId}
                    onChange={(e) => updateRow(row.key, { timeZoneId: e.target.value, daylightSaving: undefined })}
                  />
                </td>
                <td className="px-3 py-1.5 text-right">
                  <Button
                    type="button"
                    variant="ghost"
                    size="icon"
                    aria-label="Remove row"
                    className="size-8"
                    onClick={() => removeRow(row.key)}
                    disabled={rows.length === 1}
                  >
                    <Trash2 className="size-4" />
                  </Button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      <Button type="button" variant="outline" size="sm" onClick={addRow}>
        <Plus className="size-4" /> Add punch
      </Button>
    </div>
  )
}
