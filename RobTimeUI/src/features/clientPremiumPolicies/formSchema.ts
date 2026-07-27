import { z } from 'zod'

export const WAIVER_POLICIES = ['NotWaivable', 'SupervisorOnly', 'EmployeeOnly', 'BothRequired'] as const

export const WAIVER_POLICY_LABELS: Record<(typeof WAIVER_POLICIES)[number], string> = {
  NotWaivable: 'Never waivable',
  SupervisorOnly: 'Waivable with supervisor approval',
  EmployeeOnly: 'Waivable with employee waiver',
  BothRequired: 'Waivable with both supervisor approval and employee waiver',
}

// Mirrors ClientPremiumPolicyRequestValidator — the server stays authoritative (premium code
// registration and per-(client, code) overlap can only be checked there); this just gives fast
// feedback on the shape.
export const clientPremiumPolicyFormSchema = z
  .object({
    premiumCode: z.string().trim().min(1, 'Premium is required.'),
    waiverPolicy: z.enum(WAIVER_POLICIES),
    effectiveFrom: z.string().min(1, 'Effective from is required.'),
    effectiveTo: z.string().optional(),
    justification: z.string().trim().optional(),
  })
  .refine((v) => !v.effectiveTo || v.effectiveTo >= v.effectiveFrom, {
    message: 'The end date cannot be before the start date.',
    path: ['effectiveTo'],
  })

export type ClientPremiumPolicyFormValues = z.infer<typeof clientPremiumPolicyFormSchema>

export const DEFAULT_CLIENT_PREMIUM_POLICY_FORM_VALUES: ClientPremiumPolicyFormValues = {
  premiumCode: '',
  waiverPolicy: 'NotWaivable',
  effectiveFrom: '',
  effectiveTo: '',
  justification: '',
}
