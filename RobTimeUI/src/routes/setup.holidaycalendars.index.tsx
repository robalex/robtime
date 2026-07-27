import { createFileRoute, Link } from '@tanstack/react-router'
import { useHolidayCalendars } from '@/features/holidayCalendars/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { toApiProblem } from '@/lib/problem'
import { buttonVariants } from '@/components/ui/button'

export const Route = createFileRoute('/setup/holidaycalendars/')({
  component: HolidayCalendarsIndex,
})

function HolidayCalendarsIndex() {
  return <RequiresClient>{(clientId) => <HolidayCalendarList clientId={clientId} />}</RequiresClient>
}

function HolidayCalendarList({ clientId }: { clientId: number }) {
  // A small, bounded set per client (a handful of named calendars) — a single page of 100 is plenty.
  const { data, isPending, isError, error } = useHolidayCalendars({ clientId, page: 1, pageSize: 100 })

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <Link to="/setup" className="text-sm text-muted-foreground underline-offset-4 hover:underline">
            ← Setup
          </Link>
          <h1 className="text-2xl font-semibold tracking-tight">Holiday calendars</h1>
          <p className="text-muted-foreground">
            Observed dates for the "Holidays" differential mode and holiday-specific premiums.
          </p>
        </div>
        <Link to="/setup/holidaycalendars/new" className={buttonVariants()}>
          New holiday calendar
        </Link>
      </div>

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load holiday calendars.').message}
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
                <th className="px-4 py-2 font-medium">Dates</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((calendar) => (
                <tr key={calendar.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/holidaycalendars/$holidayCalendarId"
                      params={{ holidayCalendarId: String(calendar.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {calendar.name}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">
                    {calendar.dates.length} date{calendar.dates.length === 1 ? '' : 's'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">No holiday calendars yet.</p>
      )}
    </div>
  )
}
