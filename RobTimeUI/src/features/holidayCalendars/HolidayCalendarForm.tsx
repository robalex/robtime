import { useEffect, useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { useUsFederalHolidays } from './queries'
import {
  DEFAULT_HOLIDAY_CALENDAR_FORM_VALUES,
  holidayCalendarFormSchema,
  type HolidayCalendarFormValues,
} from './formSchema'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

export type { HolidayCalendarFormValues } from './formSchema'

interface HolidayCalendarFormProps {
  defaultValues?: HolidayCalendarFormValues
  submitLabel: string
  onSubmit: (values: HolidayCalendarFormValues) => Promise<unknown>
  onCancel: () => void
}

export function HolidayCalendarForm({ defaultValues, submitLabel, onSubmit, onCancel }: HolidayCalendarFormProps) {
  const form = useForm<HolidayCalendarFormValues>({
    resolver: zodResolver(holidayCalendarFormSchema),
    defaultValues: defaultValues ?? DEFAULT_HOLIDAY_CALENDAR_FORM_VALUES,
  })
  const dates = form.watch('dates')
  const errors = form.formState.errors

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this holiday calendar.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof HolidayCalendarFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-2xl space-y-6">
      <div className="space-y-2">
        <Label htmlFor="holiday-name">Name</Label>
        <Input
          id="holiday-name"
          autoFocus
          placeholder="Federal"
          aria-invalid={errors.name !== undefined}
          {...form.register('name')}
        />
        {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
      </div>

      <DatesEditor dates={dates} onChange={(next) => form.setValue('dates', next, { shouldDirty: true })} />

      {errors.root && <p className="text-sm text-destructive">{errors.root.message}</p>}

      <div className="flex gap-2">
        <Button type="submit" disabled={form.formState.isSubmitting}>
          {form.formState.isSubmitting ? 'Saving…' : submitLabel}
        </Button>
        <Button type="button" variant="outline" onClick={onCancel}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

function DatesEditor({ dates, onChange }: { dates: string[]; onChange: (next: string[]) => void }) {
  const currentYear = new Date().getFullYear()
  const [presetYear, setPresetYear] = useState(String(currentYear))
  const [pendingYear, setPendingYear] = useState<number | null>(null)
  const { data: fetchedFederalDates, isFetching } = useUsFederalHolidays(pendingYear)

  const addFederalHolidays = () => {
    setPendingYear(Number(presetYear))
  }

  // Once the query for the requested year resolves, merge it in and reset — the fetch is triggered
  // by the button click, but the merge has to happen once useUsFederalHolidays actually has data.
  useEffect(() => {
    if (fetchedFederalDates && pendingYear !== null) {
      onChange([...new Set([...dates, ...fetchedFederalDates])].sort())
      setPendingYear(null)
    }
    // dates deliberately excluded — this effect fires once per pendingYear resolving, not on every
    // dates change (which would happen right after onChange runs, forming a needless extra pass).
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [fetchedFederalDates, pendingYear])

  return (
    <div className="space-y-2">
      <Label htmlFor="holiday-addDate">Dates</Label>
      <Input
        id="holiday-addDate"
        type="date"
        onChange={(event) => {
          const value = event.target.value
          if (value && !dates.includes(value)) {
            onChange([...dates, value].sort())
          }
          event.target.value = ''
        }}
      />

      <div className="flex items-center gap-2">
        <Input
          type="number"
          value={presetYear}
          onChange={(event) => setPresetYear(event.target.value)}
          className="w-24"
          aria-label="Year for federal holiday preset"
        />
        <Button type="button" variant="outline" size="sm" disabled={isFetching} onClick={addFederalHolidays}>
          Add US federal holidays
        </Button>
      </div>

      {dates.length > 0 && (
        <ul className="flex flex-wrap gap-2 pt-1">
          {dates.map((date) => (
            <li key={date} className="flex items-center gap-1 rounded-full border bg-muted/50 px-2 py-0.5 text-sm">
              {date}
              <button
                type="button"
                onClick={() => onChange(dates.filter((d) => d !== date))}
                className="text-muted-foreground hover:text-foreground"
                aria-label={`Remove ${date}`}
              >
                ×
              </button>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
