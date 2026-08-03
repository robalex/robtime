import { z } from 'zod'

export const PROVIDERS = ['GenericCsv', 'Adp', 'Paychex', 'Gusto'] as const

export const PROVIDER_LABELS: Record<(typeof PROVIDERS)[number], string> = {
  GenericCsv: 'Generic CSV',
  Adp: 'ADP',
  Paychex: 'Paychex',
  Gusto: 'Gusto',
}

export const GROUPINGS = ['PayPeriod', 'WorkDate'] as const

export const GROUPING_LABELS: Record<(typeof GROUPINGS)[number], string> = {
  PayPeriod: 'One row per pay period',
  WorkDate: 'One row per work date',
}

export const ROUNDING_POLICIES = ['DistributeRemainder', 'AdjustmentRow'] as const

export const ROUNDING_POLICY_LABELS: Record<(typeof ROUNDING_POLICIES)[number], string> = {
  DistributeRemainder: 'Distribute the rounding remainder across rows',
  AdjustmentRow: 'Carry the rounding remainder on a single adjustment row',
}

// Mirrors PayrollExportProfileRequestValidator — the server stays authoritative (an adjustment
// earning code is required only when RoundingPolicy is AdjustmentRow); this just gives fast feedback
// on the shape.
export const payrollExportProfileFormSchema = z
  .object({
    name: z.string().trim().min(1, 'Name is required.'),
    provider: z.enum(PROVIDERS),
    grouping: z.enum(GROUPINGS),
    roundingPolicy: z.enum(ROUNDING_POLICIES),
    adjustmentEarningCode: z.string().trim().optional(),
    amountScale: z.coerce.number({ message: 'Amount scale must be a number.' }).int().min(0),
    hoursScale: z.coerce.number({ message: 'Hours scale must be a number.' }).int().min(0),
  })
  .refine((v) => v.roundingPolicy !== 'AdjustmentRow' || !!v.adjustmentEarningCode, {
    message: 'An adjustment earning code is required for this rounding policy.',
    path: ['adjustmentEarningCode'],
  })

export type PayrollExportProfileFormValues = z.infer<typeof payrollExportProfileFormSchema>

export const DEFAULT_PAYROLL_EXPORT_PROFILE_FORM_VALUES: PayrollExportProfileFormValues = {
  name: '',
  provider: 'GenericCsv',
  grouping: 'PayPeriod',
  roundingPolicy: 'DistributeRemainder',
  adjustmentEarningCode: '',
  amountScale: 2,
  hoursScale: 2,
}
