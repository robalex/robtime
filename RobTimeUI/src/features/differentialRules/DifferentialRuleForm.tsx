import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import {
  ADJUSTMENT_LABELS,
  ADJUSTMENT_TYPES,
  DAY_SCHEDULE_MODES,
  DAYS_OF_WEEK,
  DEFAULT_DIFFERENTIAL_RULE_FORM_VALUES,
  differentialRuleFormSchema,
  MODE_LABELS,
  type DifferentialRuleFormValues,
} from './formSchema'

export type { DifferentialRuleFormValues } from './formSchema'

interface DifferentialRuleFormProps {
  defaultValues?: DifferentialRuleFormValues
  submitLabel: string
  onSubmit: (values: DifferentialRuleFormValues) => Promise<unknown>
  onCancel: () => void
}

export function DifferentialRuleForm({
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: DifferentialRuleFormProps) {
  const form = useForm<DifferentialRuleFormValues>({
    resolver: zodResolver(differentialRuleFormSchema),
    defaultValues: defaultValues ?? DEFAULT_DIFFERENTIAL_RULE_FORM_VALUES,
  })
  const values = form.watch()
  const errors = form.formState.errors

  const submit = form.handleSubmit(async (submitted) => {
    try {
      await onSubmit(submitted.allDay ? { ...submitted, windowStart: '00:00:00', windowEnd: '00:00:00' } : submitted)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this differential rule.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in submitted) {
          form.setError(field as keyof DifferentialRuleFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-2xl space-y-8">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="diff-code">Code</Label>
          <Input
            id="diff-code"
            autoFocus
            placeholder="NIGHT_SHIFT"
            aria-invalid={errors.code !== undefined}
            {...form.register('code')}
          />
          {errors.code && <p className="text-sm text-destructive">{errors.code.message}</p>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="diff-adjustmentType">Adjustment type</Label>
          <Select id="diff-adjustmentType" {...form.register('adjustmentType')}>
            {ADJUSTMENT_TYPES.map((type) => (
              <option key={type} value={type}>
                {ADJUSTMENT_LABELS[type]}
              </option>
            ))}
          </Select>
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="diff-adjustmentValue">
            {values.adjustmentType === 'Multiplier' ? 'Extra fraction of base rate' : 'Adjustment amount ($)'}
          </Label>
          <Input
            id="diff-adjustmentValue"
            type="number"
            step="0.01"
            min="0"
            aria-invalid={errors.adjustmentValue !== undefined}
            {...form.register('adjustmentValue')}
          />
          {errors.adjustmentValue && (
            <p className="text-sm text-destructive">{errors.adjustmentValue.message}</p>
          )}
        </div>
        <div className="space-y-2">
          <Label htmlFor="diff-exclusivityGroup">Exclusivity group</Label>
          <Input id="diff-exclusivityGroup" placeholder="Optional" {...form.register('exclusivityGroup')} />
          <p className="text-xs text-muted-foreground">
            Differentials sharing a group are mutually exclusive on a shift — only the highest-amount
            one applies. Leave blank to always stack with other differentials.
          </p>
        </div>
      </div>

      <fieldset className="space-y-4 rounded-lg border p-4">
        <legend className="px-1 text-sm font-medium">When it's active</legend>

        <div className="space-y-2">
          <Label htmlFor="diff-dayScheduleMode">Day schedule</Label>
          <Select id="diff-dayScheduleMode" {...form.register('dayScheduleMode')}>
            {DAY_SCHEDULE_MODES.map((mode) => (
              <option key={mode} value={mode}>
                {MODE_LABELS[mode]}
              </option>
            ))}
          </Select>
        </div>

        {values.dayScheduleMode === 'DaysOfWeek' && (
          <div className="space-y-2">
            <div className="flex flex-wrap gap-3 rounded-md border p-3">
              {DAYS_OF_WEEK.map((day) => (
                <label key={day} className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4 rounded border-input"
                    value={day}
                    checked={values.daysOfWeek.includes(day)}
                    onChange={(event) => {
                      const next = event.target.checked
                        ? [...values.daysOfWeek, day]
                        : values.daysOfWeek.filter((d) => d !== day)
                      form.setValue('daysOfWeek', next, { shouldDirty: true })
                    }}
                  />
                  {day}
                </label>
              ))}
            </div>
            {errors.daysOfWeek && <p className="text-sm text-destructive">{errors.daysOfWeek.message}</p>}
          </div>
        )}

        {values.dayScheduleMode === 'ConsecutiveDayRange' && (
          <div className="space-y-4">
            <div className="grid gap-4 sm:grid-cols-2">
              <div className="space-y-2">
                <Label htmlFor="diff-rangeStart">Range start</Label>
                <Select id="diff-rangeStart" {...form.register('dayOfWeekRangeStart')}>
                  {DAYS_OF_WEEK.map((day) => (
                    <option key={day} value={day}>
                      {day}
                    </option>
                  ))}
                </Select>
              </div>
              <div className="space-y-2">
                <Label htmlFor="diff-rangeEnd">Range end</Label>
                <Select id="diff-rangeEnd" {...form.register('dayOfWeekRangeEnd')}>
                  {DAYS_OF_WEEK.map((day) => (
                    <option key={day} value={day}>
                      {day}
                    </option>
                  ))}
                </Select>
                {errors.dayOfWeekRangeEnd && (
                  <p className="text-sm text-destructive">{errors.dayOfWeekRangeEnd.message}</p>
                )}
              </div>
            </div>
            <div className="space-y-2">
              <Label htmlFor="diff-minHoursInRange">Minimum qualifying hours across the range</Label>
              <Input
                id="diff-minHoursInRange"
                type="number"
                step="0.5"
                min="0"
                aria-invalid={errors.minHoursInRange !== undefined}
                {...form.register('minHoursInRange')}
              />
              <p className="text-xs text-muted-foreground">
                0 means no range threshold — every qualifying shift in the range earns the
                differential independently.
              </p>
              {errors.minHoursInRange && (
                <p className="text-sm text-destructive">{errors.minHoursInRange.message}</p>
              )}
            </div>
          </div>
        )}

        {values.dayScheduleMode === 'SpecificDates' && (
          <SpecificDatesEditor
            dates={values.specificDates}
            onChange={(next) => form.setValue('specificDates', next, { shouldDirty: true })}
            error={errors.specificDates?.message}
          />
        )}

        {values.dayScheduleMode === 'Holidays' && (
          <p className="text-sm text-muted-foreground">
            Active on any date flagged in the client's holiday calendar.
          </p>
        )}
      </fieldset>

      <fieldset className="space-y-4 rounded-lg border p-4">
        <legend className="px-1 text-sm font-medium">Time-of-day window</legend>

        <div className="flex items-center gap-2">
          <input
            id="diff-allDay"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...form.register('allDay')}
          />
          <Label htmlFor="diff-allDay" className="font-normal">
            All day — no time-of-day restriction
          </Label>
        </div>

        {!values.allDay && (
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="diff-windowStart">Window start</Label>
              <Input id="diff-windowStart" type="time" step="1" {...form.register('windowStart')} />
            </div>
            <div className="space-y-2">
              <Label htmlFor="diff-windowEnd">Window end</Label>
              <Input id="diff-windowEnd" type="time" step="1" {...form.register('windowEnd')} />
              <p className="text-xs text-muted-foreground">
                End before start means the window wraps past midnight (e.g. 18:00–06:00 for a night
                differential).
              </p>
            </div>
          </div>
        )}

        {values.dayScheduleMode !== 'ConsecutiveDayRange' && (
          <div className="space-y-2">
            <Label htmlFor="diff-minHoursInWindow">Minimum hours worked inside the window</Label>
            <Input
              id="diff-minHoursInWindow"
              type="number"
              step="0.5"
              min="0"
              aria-invalid={errors.minHoursInWindow !== undefined}
              {...form.register('minHoursInWindow')}
            />
            <p className="text-xs text-muted-foreground">
              Within a single shift. 0 means any amount of qualifying time earns the differential.
            </p>
            {errors.minHoursInWindow && (
              <p className="text-sm text-destructive">{errors.minHoursInWindow.message}</p>
            )}
          </div>
        )}
      </fieldset>

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

function SpecificDatesEditor({
  dates,
  onChange,
  error,
}: {
  dates: string[]
  onChange: (next: string[]) => void
  error?: string
}) {
  return (
    <div className="space-y-2">
      <Label htmlFor="diff-addDate">Dates</Label>
      <Input
        id="diff-addDate"
        type="date"
        onChange={(event) => {
          const value = event.target.value
          if (value && !dates.includes(value)) {
            onChange([...dates, value].sort())
          }
          event.target.value = ''
        }}
      />
      {dates.length > 0 && (
        <ul className="flex flex-wrap gap-2">
          {dates.map((date) => (
            <li
              key={date}
              className="flex items-center gap-1 rounded-full border bg-muted/50 px-2 py-0.5 text-sm"
            >
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
      {error && <p className="text-sm text-destructive">{error}</p>}
    </div>
  )
}
