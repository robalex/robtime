import { z } from 'zod'

// Mirrors StateMinimumWageRequestValidator — the server stays authoritative.
export const stateMinimumWageFormSchema = z.object({
  state: z.string().trim().min(1, 'State is required.'),
  effectiveFrom: z.string().min(1, 'Effective from is required.'),
  effectiveTo: z.string().optional(),
  amount: z.coerce
    .number({ message: 'Amount must be a number.' })
    .nonnegative('Amount cannot be negative.'),
})

export type StateMinimumWageFormValues = z.infer<typeof stateMinimumWageFormSchema>
