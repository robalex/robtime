import { z } from 'zod'

export const LINE_TYPES = ['Regular', 'OvertimePremium', 'Differential', 'Bonus', 'FixedHours', 'Premium'] as const

export const LINE_TYPE_LABELS: Record<(typeof LINE_TYPES)[number], string> = {
  Regular: 'Regular',
  OvertimePremium: 'Overtime premium',
  Differential: 'Differential',
  Bonus: 'Bonus',
  FixedHours: 'Fixed hours (e.g. paid leave)',
  Premium: 'Meal/rest premium',
}

export const VALUE_BASES = ['Hours', 'Amount'] as const

export const VALUE_BASIS_LABELS: Record<(typeof VALUE_BASES)[number], string> = {
  Hours: 'Hours',
  Amount: 'Amount ($)',
}

// The exact required shape of lineCode (e.g. "" for Regular, "OVERTIME"/"DOUBLETIME" for
// OvertimePremium, a real BonusKind/DifferentialRule/premium code for the others) is server-
// validated only — PayrollEarningCodeMappingRequestValidator is the source of truth, and duplicating
// its per-type rules here would drift out of sync with the engine's own registries. The 400 field
// error surfaces through the normal toApiProblem flow.
export const payrollEarningCodeMappingFormSchema = z.object({
  lineType: z.enum(LINE_TYPES),
  lineCode: z.string(),
  earningCode: z.string().trim().min(1, 'Earning code is required.'),
  valueBasis: z.enum(VALUE_BASES),
  description: z.string().trim().optional(),
})

export type PayrollEarningCodeMappingFormValues = z.infer<typeof payrollEarningCodeMappingFormSchema>

export const DEFAULT_PAYROLL_EARNING_CODE_MAPPING_FORM_VALUES: PayrollEarningCodeMappingFormValues = {
  lineType: 'Regular',
  lineCode: '',
  earningCode: '',
  valueBasis: 'Hours',
  description: '',
}
