import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { HolidayCalendarForm } from '@/features/holidayCalendars/HolidayCalendarForm'
import { DEFAULT_HOLIDAY_CALENDAR_FORM_VALUES } from '@/features/holidayCalendars/formSchema'
import { useCreateHolidayCalendar } from '@/features/holidayCalendars/queries'
import { RequiresClient } from '@/components/RequiresClient'

export const Route = createFileRoute('/setup/holidaycalendars/new')({
  component: NewHolidayCalendar,
})

function NewHolidayCalendar() {
  return <RequiresClient>{(clientId) => <NewHolidayCalendarForm clientId={clientId} />}</RequiresClient>
}

function NewHolidayCalendarForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const createHolidayCalendar = useCreateHolidayCalendar()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New holiday calendar</h1>
      <HolidayCalendarForm
        defaultValues={DEFAULT_HOLIDAY_CALENDAR_FORM_VALUES}
        submitLabel="Create holiday calendar"
        onCancel={() => void navigate({ to: '/setup/holidaycalendars' })}
        onSubmit={async (values) => {
          await createHolidayCalendar.mutateAsync({ clientId, name: values.name, dates: values.dates })
          await navigate({ to: '/setup/holidaycalendars' })
        }}
      />
    </div>
  )
}
