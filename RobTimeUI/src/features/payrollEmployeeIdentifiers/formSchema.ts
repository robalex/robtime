import { z } from 'zod'

// Mirrors PayrollEmployeeIdentifierRequestValidator's shape check (non-empty, no comma — a comma
// would break a generic CSV export). employeeId is only present/required on create — Update never
// takes it (re-pointing an identifier to a different employee is delete-and-recreate, per the
// backend contract's own doc comment), so it's optional here and the create route enforces it.
export const payrollEmployeeIdentifierFormSchema = z.object({
  // Coerced from a <select>'s string value; an unselected "" option coerces to 0, which fails
  // .positive() — that's how a missing selection is caught, since the field is otherwise .optional()
  // (edit mode never renders the select at all, so its value stays undefined there and passes).
  employeeId: z.coerce
    .number({ message: 'Employee is required.' })
    .int()
    .positive({ message: 'Employee is required.' })
    .optional(),
  externalEmployeeId: z
    .string()
    .trim()
    .min(1, 'External employee id is required.')
    .refine((v) => !v.includes(','), 'External employee id cannot contain a comma.'),
})

export type PayrollEmployeeIdentifierFormValues = z.infer<typeof payrollEmployeeIdentifierFormSchema>

export const DEFAULT_PAYROLL_EMPLOYEE_IDENTIFIER_FORM_VALUES: PayrollEmployeeIdentifierFormValues = {
  employeeId: undefined,
  externalEmployeeId: '',
}
