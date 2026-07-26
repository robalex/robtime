import { z } from 'zod'

export const DAY_SCHEDULE_MODES = ['EveryDay', 'DaysOfWeek', 'ConsecutiveDayRange', 'SpecificDates', 'Holidays'] as const
// NodaTime's IsoDayOfWeek also has a "None" member (0) — never a value the form itself produces,
// but the wire type includes it, so callers converting an API response into form values need it to
// fall back on (see setup.differentialrules.$differentialRuleId.tsx's toFormValues).
export const DAYS_OF_WEEK = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'] as const
export const ADJUSTMENT_TYPES = ['FlatPerHour', 'Multiplier', 'FixedBonus'] as const

export const MODE_LABELS: Record<(typeof DAY_SCHEDULE_MODES)[number], string> = {
  EveryDay: 'Every day',
  DaysOfWeek: 'Specific days of the week',
  ConsecutiveDayRange: 'Consecutive day range (e.g. a long weekend)',
  SpecificDates: 'Specific dates',
  Holidays: "The client's holiday calendar",
}

export const ADJUSTMENT_LABELS: Record<(typeof ADJUSTMENT_TYPES)[number], string> = {
  FlatPerHour: 'Flat amount per qualifying hour',
  Multiplier: 'Multiplier on the base rate per qualifying hour',
  FixedBonus: 'One-time bonus when the shift qualifies',
}

// Mirrors DifferentialRuleRequestValidator (both the flat request checks and ValidateConsistency's
// per-mode checks) — the server stays authoritative; this just gives fast feedback in the form.
export const differentialRuleFormSchema = z
  .object({
    code: z.string().trim().min(1, 'Code is required.'),
    dayScheduleMode: z.enum(DAY_SCHEDULE_MODES),
    daysOfWeek: z.array(z.enum(DAYS_OF_WEEK)),
    dayOfWeekRangeStart: z.enum(DAYS_OF_WEEK),
    dayOfWeekRangeEnd: z.enum(DAYS_OF_WEEK),
    specificDates: z.array(z.string()),
    allDay: z.boolean(),
    windowStart: z.string(),
    windowEnd: z.string(),
    adjustmentType: z.enum(ADJUSTMENT_TYPES),
    adjustmentValue: z.coerce
      .number({ message: 'Adjustment value must be a number.' })
      .nonnegative('Adjustment value cannot be negative.'),
    minHoursInWindow: z.coerce
      .number({ message: 'Must be a number.' })
      .nonnegative('Minimum hours in window cannot be negative.'),
    minHoursInRange: z.coerce
      .number({ message: 'Must be a number.' })
      .nonnegative('Minimum hours in range cannot be negative.'),
    exclusivityGroup: z.string().trim().optional(),
  })
  .refine((v) => v.dayScheduleMode !== 'DaysOfWeek' || v.daysOfWeek.length > 0, {
    message: 'At least one day of week is required for this mode.',
    path: ['daysOfWeek'],
  })
  .refine((v) => v.dayScheduleMode !== 'ConsecutiveDayRange' || v.dayOfWeekRangeStart !== v.dayOfWeekRangeEnd, {
    message: "Range start and end can't be the same day — use the day-of-week mode for a single day.",
    path: ['dayOfWeekRangeEnd'],
  })
  .refine((v) => v.dayScheduleMode !== 'SpecificDates' || v.specificDates.length > 0, {
    message: 'At least one date is required for this mode.',
    path: ['specificDates'],
  })

export type DifferentialRuleFormValues = z.infer<typeof differentialRuleFormSchema>

export const DEFAULT_DIFFERENTIAL_RULE_FORM_VALUES: DifferentialRuleFormValues = {
  code: '',
  dayScheduleMode: 'EveryDay',
  daysOfWeek: [],
  dayOfWeekRangeStart: 'Thursday',
  dayOfWeekRangeEnd: 'Tuesday',
  specificDates: [],
  allDay: true,
  windowStart: '00:00:00',
  windowEnd: '00:00:00',
  adjustmentType: 'FlatPerHour',
  adjustmentValue: 0,
  minHoursInWindow: 0,
  minHoursInRange: 0,
  exclusivityGroup: '',
}
