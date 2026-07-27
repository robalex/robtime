import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useDeleteHolidayCalendar,
  useHolidayCalendar,
  useUpdateHolidayCalendar,
} from '@/features/holidayCalendars/queries'
import {
  HolidayCalendarForm,
  type HolidayCalendarFormValues,
} from '@/features/holidayCalendars/HolidayCalendarForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/holidaycalendars/$holidayCalendarId')({
  component: EditHolidayCalendar,
})

function EditHolidayCalendar() {
  const { holidayCalendarId } = Route.useParams()
  const id = Number(holidayCalendarId)
  const navigate = useNavigate()

  const { data: calendar, isPending, isError, error } = useHolidayCalendar(id)
  const updateHolidayCalendar = useUpdateHolidayCalendar(id)
  const deleteHolidayCalendar = useDeleteHolidayCalendar()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this holiday calendar.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Holiday calendar not found' : 'Could not load this holiday calendar'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/setup/holidaycalendars" className="text-sm underline underline-offset-4">
          Back to holiday calendars
        </Link>
      </div>
    )
  }

  const defaultValues: HolidayCalendarFormValues = { name: calendar.name, dates: calendar.dates }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link
          to="/setup/holidaycalendars"
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Holiday calendars
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{calendar.name}</h1>
      </div>

      <HolidayCalendarForm
        defaultValues={defaultValues}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/holidaycalendars' })}
        onSubmit={(values) => updateHolidayCalendar.mutateAsync({ name: values.name, dates: values.dates })}
      />

      <div className="max-w-2xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this holiday calendar</h2>
          <p className="text-sm text-muted-foreground">Soft-deleted.</p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteHolidayCalendar.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteHolidayCalendar.mutateAsync(id)
                  await navigate({ to: '/setup/holidaycalendars' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this holiday calendar.').message)
                }
              }}
            >
              {deleteHolidayCalendar.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete holiday calendar
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
