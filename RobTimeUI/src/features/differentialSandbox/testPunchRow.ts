import { toWireLocalDateTime } from '@/lib/dates'
import type { SandboxTestPunch } from './queries'

export interface TestPunchRow {
  key: string
  when: string // datetime-local
  kind: 'In' | 'Out'
  timeZoneId: string // '' = employee's own home zone
  // Only meaningful once a Run has flagged this row's `when` as an ambiguous fall-back hour.
  daylightSaving?: boolean
}

export function blankTestPunchRow(counter: { current: number }, when: string, kind: 'In' | 'Out' = 'In'): TestPunchRow {
  counter.current += 1
  return { key: `test-punch-${counter.current}`, when, kind, timeZoneId: '' }
}

export function toSandboxTestPunch(row: TestPunchRow): SandboxTestPunch {
  return {
    punchTime: toWireLocalDateTime(row.when),
    punchTimeZoneId: row.timeZoneId || undefined,
    daylightSaving: row.daylightSaving,
    kind: row.kind,
  }
}
