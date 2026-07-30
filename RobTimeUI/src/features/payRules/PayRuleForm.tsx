import { useState, type ComponentProps } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import type { PayRuleTemplate } from './queries'
import type { PremiumRuleMetadata } from '@/features/premiumRules/queries'
import type { DifferentialRule } from '@/features/differentialRules/queries'
import type { HolidayCalendar } from '@/features/holidayCalendars/queries'

const WORKWEEK_DAYS = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
] as const

const ROUNDING_STRATEGIES = ['None', 'NearestInterval', 'IntervalWithGrace'] as const
const SHIFT_DATE_STRATEGIES = [
  'FirstPunchLocalDate',
  'LastPunchLocalDate',
  'MajorityHoursLocalDate',
  'SplitAtMidnight',
] as const

// Mirrors PayRuleFieldsRequest — server stays authoritative. Fields the template doesn't touch stay
// optional strings (blank = send nothing = server applies PayRule's own default, §6 Rule 2's "show
// the default as placeholder, send null" discipline). Fields the template DOES set (the six below
// tracked for "modified" indicators) always carry a concrete value, since the mandatory template
// step (§6 Rule 3) means they're never genuinely unset.
const payRuleFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required.'),
  description: z.string().trim().optional(),
  workweekStartDay: z.string().min(1, 'Workweek start day is required.'),
  weeklyOvertimeThresholdHours: z
    .string()
    .min(1, 'Weekly overtime threshold is required.')
    .refine(
      (v) => !Number.isNaN(Number(v)) && Number(v) > 0,
      'Weekly overtime threshold must be a positive number.',
    ),
  hasDailyOvertime: z.boolean(),
  dailyOvertimeThresholdHours: z.string().optional(),
  dailyDoubletimeThresholdHours: z.string().optional(),
  hasSeventhDayRule: z.boolean(),
  roundingStrategy: z.string().min(1),
  roundingIntervalMinutes: z.string().optional(),
  roundingGraceMinutes: z.string().optional(),
  punchPairResetHours: z.string().optional(),
  maxShiftLengthHours: z.string().optional(),
  distanceBetweenShiftsHours: z.string().optional(),
  expectedBreakLengthMinutes: z.string().optional(),
  expectedLunchLengthMinutes: z.string().optional(),
  shiftDateStrategy: z.string().min(1),
  activePremiumCodes: z.array(z.string()),
  activeDifferentialCodes: z.array(z.string()),
  holidayCalendarId: z.string().optional(),
})

export type PayRuleFormValues = z.infer<typeof payRuleFormSchema>

// The templated fields eligible for a "modified" indicator + revert — the set PayRuleTemplateResponse
// actually presets. Every other field is independently configured and has no template value to diff
// against.
const TEMPLATED_NUMERIC_FIELDS = [
  ['weeklyOvertimeThresholdHours', 'weeklyOvertimeThresholdHours'],
  ['dailyOvertimeThresholdHours', 'dailyOvertimeThresholdHours'],
  ['dailyDoubletimeThresholdHours', 'dailyDoubletimeThresholdHours'],
] as const
const TEMPLATED_BOOLEAN_FIELDS = [
  ['hasDailyOvertime', 'hasDailyOvertime'],
  ['hasSeventhDayRule', 'hasSeventhDayRule'],
] as const

function isFieldModified(
  values: PayRuleFormValues,
  field: keyof PayRuleFormValues,
  template: PayRuleTemplate,
): boolean {
  if (field === 'activePremiumCodes') {
    const current = new Set(values.activePremiumCodes)
    const templated = new Set(template.activePremiumCodes)
    return current.size !== templated.size || [...current].some((code) => !templated.has(code))
  }
  const numericField = TEMPLATED_NUMERIC_FIELDS.find(([f]) => f === field)
  if (numericField) {
    const current = Number(values[field])
    return !Number.isNaN(current) && current !== template[numericField[1]]
  }
  const booleanField = TEMPLATED_BOOLEAN_FIELDS.find(([f]) => f === field)
  if (booleanField) {
    return values[field] !== template[booleanField[1]]
  }
  return false
}

interface PayRuleFormProps {
  /** The template this rule is based on — drives "modified" indicators + revert (§6 Rule 3). Null
   * when the rule's stored templateCode doesn't match a known template (legacy data, or the
   * registry changed) — the form still works, just without the diff affordance. */
  template: PayRuleTemplate | null
  premiumRules: PremiumRuleMetadata[]
  /** The pay rule's own client's differentials — jurisdiction templates don't preset these (they're
   * client-authored), so unlike premiums there's no "modified from template" diff for this field. */
  differentialRules: DifferentialRule[]
  /** The pay rule's own client's holiday calendars — which one (if any) this rule observes for
   * Holidays-mode differentials. */
  holidayCalendars: HolidayCalendar[]
  defaultValues: PayRuleFormValues
  submitLabel: string
  onSubmit: (values: PayRuleFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PayRuleForm({
  template,
  premiumRules,
  differentialRules,
  holidayCalendars,
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: PayRuleFormProps) {
  const form = useForm<PayRuleFormValues>({
    resolver: zodResolver(payRuleFormSchema),
    defaultValues,
  })
  const values = form.watch()

  // Computed once at mount, not on every keystroke — auto-expands Advanced if it *starts* modified
  // (§6 Rule 3: "can never hide behind a collapsed section"), but doesn't fight a user who
  // deliberately opens or closes it afterward.
  const [advancedInitiallyModified] = useState(
    () => template !== null && isFieldModified(defaultValues, 'activePremiumCodes', template),
  )

  const modifiedCount = template
    ? (Object.keys(values) as (keyof PayRuleFormValues)[]).filter((field) =>
        isFieldModified(values, field, template),
      ).length
    : 0

  const revert = (field: keyof PayRuleFormValues) => {
    if (!template) {
      return
    }
    if (field === 'activePremiumCodes') {
      form.setValue(field, [...template.activePremiumCodes], { shouldDirty: true })
      return
    }
    const numericField = TEMPLATED_NUMERIC_FIELDS.find(([f]) => f === field)
    if (numericField) {
      form.setValue(field, String(template[numericField[1]]), { shouldDirty: true })
      return
    }
    const booleanField = TEMPLATED_BOOLEAN_FIELDS.find(([f]) => f === field)
    if (booleanField) {
      form.setValue(field, template[booleanField[1]], { shouldDirty: true })
    }
  }

  const submit = form.handleSubmit(async (submitted) => {
    try {
      await onSubmit(submitted)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this pay rule.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in submitted) {
          form.setError(field as keyof PayRuleFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  const errors = form.formState.errors

  return (
    <form onSubmit={submit} className="max-w-2xl space-y-8">
      {template && (
        <p className="text-sm text-muted-foreground">
          Based on the <span className="font-medium text-foreground">{template.name}</span>{' '}
          template — {modifiedCount === 0 ? 'no settings customised.' : `${modifiedCount} setting${modifiedCount === 1 ? '' : 's'} customised.`}
        </p>
      )}

      {/* Essential — always visible (§6 Rule 2). */}
      <div className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Name" name="name" form={form} error={errors.name} autoFocus />
          <TextField label="Description" name="description" form={form} error={errors.description} />
        </div>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="space-y-2">
            <Label htmlFor="payrule-workweekStartDay">Workweek start day</Label>
            <Select id="payrule-workweekStartDay" {...form.register('workweekStartDay')}>
              {WORKWEEK_DAYS.map((day) => (
                <option key={day} value={day}>
                  {day}
                </option>
              ))}
            </Select>
          </div>
          <ModifiedField
            label="Weekly overtime threshold (hours)"
            name="weeklyOvertimeThresholdHours"
            form={form}
            error={errors.weeklyOvertimeThresholdHours}
            modified={template !== null && isFieldModified(values, 'weeklyOvertimeThresholdHours', template)}
            onRevert={() => revert('weeklyOvertimeThresholdHours')}
            type="number"
            step="0.5"
            min="0"
          />
        </div>
      </div>

      {/* Common — visible, grouped (§6 Rule 2). */}
      <fieldset className="space-y-4 rounded-lg border p-4">
        <legend className="px-1 text-sm font-medium">Overtime & rounding</legend>

        <div className="flex items-center gap-2">
          <input
            id="payrule-hasDailyOvertime"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...form.register('hasDailyOvertime')}
          />
          <Label htmlFor="payrule-hasDailyOvertime" className="font-normal">
            Daily overtime
          </Label>
          {template !== null && isFieldModified(values, 'hasDailyOvertime', template) && (
            <ModifiedBadge onRevert={() => revert('hasDailyOvertime')} />
          )}
        </div>

        {values.hasDailyOvertime && (
          <div className="grid gap-4 pl-6 sm:grid-cols-2">
            <ModifiedField
              label="Daily overtime threshold (hours)"
              name="dailyOvertimeThresholdHours"
              form={form}
              error={errors.dailyOvertimeThresholdHours}
              modified={template !== null && isFieldModified(values, 'dailyOvertimeThresholdHours', template)}
              onRevert={() => revert('dailyOvertimeThresholdHours')}
              type="number"
              step="0.5"
              min="0"
              placeholder="8"
            />
            <ModifiedField
              label="Daily double-time threshold (hours)"
              name="dailyDoubletimeThresholdHours"
              form={form}
              error={errors.dailyDoubletimeThresholdHours}
              modified={
                template !== null && isFieldModified(values, 'dailyDoubletimeThresholdHours', template)
              }
              onRevert={() => revert('dailyDoubletimeThresholdHours')}
              type="number"
              step="0.5"
              min="0"
              placeholder="12"
            />
          </div>
        )}

        <div className="flex items-center gap-2">
          <input
            id="payrule-hasSeventhDayRule"
            type="checkbox"
            className="h-4 w-4 rounded border-input"
            {...form.register('hasSeventhDayRule')}
          />
          <Label htmlFor="payrule-hasSeventhDayRule" className="font-normal">
            7th consecutive day overtime
          </Label>
          {template !== null && isFieldModified(values, 'hasSeventhDayRule', template) && (
            <ModifiedBadge onRevert={() => revert('hasSeventhDayRule')} />
          )}
        </div>

        <div className="grid gap-4 sm:grid-cols-3">
          <div className="space-y-2">
            <Label htmlFor="payrule-roundingStrategy">Rounding strategy</Label>
            <Select id="payrule-roundingStrategy" {...form.register('roundingStrategy')}>
              {ROUNDING_STRATEGIES.map((strategy) => (
                <option key={strategy} value={strategy}>
                  {strategy}
                </option>
              ))}
            </Select>
          </div>
          <TextField
            label="Interval (minutes)"
            name="roundingIntervalMinutes"
            form={form}
            error={errors.roundingIntervalMinutes}
            type="number"
            min="1"
            placeholder="15"
          />
          <TextField
            label="Grace (minutes)"
            name="roundingGraceMinutes"
            form={form}
            error={errors.roundingGraceMinutes}
            type="number"
            min="0"
            placeholder="7"
          />
        </div>
      </fieldset>

      {/* Advanced — collapsed, auto-expands if it starts with a modified field (§6 Rule 2 / Rule 3).
          Native details/summary: keyboard- and screen-reader-accessible with no JS dependency. */}
      <details className="rounded-lg border p-4" open={advancedInitiallyModified}>
        <summary className="cursor-pointer text-sm font-medium">
          Advanced — punch pairing, shifts, state premiums, and differentials
        </summary>
        <div className="mt-4 space-y-6">
          <div className="grid gap-4 sm:grid-cols-3">
            <TextField
              label="Punch pair reset (hours)"
              name="punchPairResetHours"
              form={form}
              error={errors.punchPairResetHours}
              type="number"
              min="0"
              placeholder="15"
            />
            <TextField
              label="Max shift length (hours)"
              name="maxShiftLengthHours"
              form={form}
              error={errors.maxShiftLengthHours}
              type="number"
              min="0"
              placeholder="15"
            />
            <TextField
              label="Distance between shifts (hours)"
              name="distanceBetweenShiftsHours"
              form={form}
              error={errors.distanceBetweenShiftsHours}
              type="number"
              min="0"
              placeholder="6"
            />
            <TextField
              label="Expected break length (minutes)"
              name="expectedBreakLengthMinutes"
              form={form}
              error={errors.expectedBreakLengthMinutes}
              type="number"
              min="0"
              placeholder="15"
            />
            <TextField
              label="Expected lunch length (minutes)"
              name="expectedLunchLengthMinutes"
              form={form}
              error={errors.expectedLunchLengthMinutes}
              type="number"
              min="0"
              placeholder="30"
            />
            <div className="space-y-2">
              <Label htmlFor="payrule-shiftDateStrategy">Shift date strategy</Label>
              <Select id="payrule-shiftDateStrategy" {...form.register('shiftDateStrategy')}>
                {SHIFT_DATE_STRATEGIES.map((strategy) => (
                  <option key={strategy} value={strategy}>
                    {strategy}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="payrule-holidayCalendarId">Holiday calendar</Label>
              <Select id="payrule-holidayCalendarId" {...form.register('holidayCalendarId')}>
                <option value="">None</option>
                {holidayCalendars.map((calendar) => (
                  <option key={calendar.id} value={calendar.id}>
                    {calendar.name}
                  </option>
                ))}
              </Select>
            </div>
          </div>

          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <Label>State premiums</Label>
              {template !== null && isFieldModified(values, 'activePremiumCodes', template) && (
                <ModifiedBadge onRevert={() => revert('activePremiumCodes')} />
              )}
            </div>
            <div className="space-y-2 rounded-md border p-3">
              {premiumRules.map((rule) => (
                <label key={rule.code} className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="mt-0.5 h-4 w-4 rounded border-input"
                    value={rule.code}
                    checked={values.activePremiumCodes.includes(rule.code)}
                    onChange={(event) => {
                      const next = event.target.checked
                        ? [...values.activePremiumCodes, rule.code]
                        : values.activePremiumCodes.filter((code) => code !== rule.code)
                      form.setValue('activePremiumCodes', next, { shouldDirty: true })
                    }}
                  />
                  <span>
                    <span className="font-medium">{rule.name}</span>{' '}
                    <span className="text-muted-foreground">({rule.jurisdiction})</span>
                    <br />
                    <span className="text-muted-foreground">{rule.description}</span>
                  </span>
                </label>
              ))}
            </div>
          </div>

          <div className="space-y-2">
            <Label>Differentials</Label>
            <div className="space-y-2 rounded-md border p-3">
              {differentialRules.length === 0 && (
                <p className="text-sm text-muted-foreground">
                  This client has no differential rules yet.
                </p>
              )}
              {differentialRules.map((rule) => (
                <label key={rule.code} className="flex items-start gap-2 text-sm">
                  <input
                    type="checkbox"
                    className="mt-0.5 h-4 w-4 rounded border-input"
                    value={rule.code}
                    checked={values.activeDifferentialCodes.includes(rule.code)}
                    onChange={(event) => {
                      const next = event.target.checked
                        ? [...values.activeDifferentialCodes, rule.code]
                        : values.activeDifferentialCodes.filter((code) => code !== rule.code)
                      form.setValue('activeDifferentialCodes', next, { shouldDirty: true })
                    }}
                  />
                  <span className="font-medium">{rule.code}</span>
                </label>
              ))}
            </div>
          </div>
        </div>
      </details>

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

function ModifiedBadge({ onRevert }: { onRevert: () => void }) {
  return (
    <button
      type="button"
      onClick={onRevert}
      className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-2 py-0.5 text-xs font-medium text-primary hover:bg-primary/20"
      title="Different from the template. Click to reset."
    >
      <span aria-hidden className="h-1.5 w-1.5 rounded-full bg-primary" />
      modified — reset
    </button>
  )
}

interface TextFieldProps extends Omit<ComponentProps<'input'>, 'name' | 'form'> {
  label: string
  name: keyof PayRuleFormValues
  form: ReturnType<typeof useForm<PayRuleFormValues>>
  error?: { message?: string }
}

function TextField({ label, name, form, error, ...inputProps }: TextFieldProps) {
  const id = `payrule-${name}`
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        aria-invalid={error !== undefined}
        {...form.register(name as never)}
        {...inputProps}
      />
      {error && <p className="text-sm text-destructive">{error.message}</p>}
    </div>
  )
}

interface ModifiedFieldProps extends TextFieldProps {
  modified: boolean
  onRevert: () => void
}

function ModifiedField({ modified, onRevert, label, name, form, error, ...inputProps }: ModifiedFieldProps) {
  const id = `payrule-${name}`
  return (
    <div className="space-y-2">
      <div className="flex items-center gap-2">
        <Label htmlFor={id}>{label}</Label>
        {modified && <ModifiedBadge onRevert={onRevert} />}
      </div>
      <Input
        id={id}
        aria-invalid={error !== undefined}
        {...form.register(name as never)}
        {...inputProps}
      />
      {error && <p className="text-sm text-destructive">{error.message}</p>}
    </div>
  )
}
