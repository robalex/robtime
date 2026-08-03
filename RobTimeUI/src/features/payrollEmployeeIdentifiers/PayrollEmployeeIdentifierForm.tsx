import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import {
  DEFAULT_PAYROLL_EMPLOYEE_IDENTIFIER_FORM_VALUES,
  payrollEmployeeIdentifierFormSchema,
  type PayrollEmployeeIdentifierFormValues,
} from './formSchema'

export type { PayrollEmployeeIdentifierFormValues } from './formSchema'

interface EmployeeOption {
  id: number
  firstName: string
  lastName: string
}

interface PayrollEmployeeIdentifierFormProps {
  defaultValues?: PayrollEmployeeIdentifierFormValues
  submitLabel: string
  onSubmit: (values: PayrollEmployeeIdentifierFormValues) => Promise<unknown>
  onCancel: () => void
  /** Create mode: the pickable employee list. Omit together with providing lockedEmployeeName. */
  employees?: EmployeeOption[]
  /**
   * Edit mode: which employee this identifier is fixed to (UpdatePayrollEmployeeIdentifierRequest
   * has no EmployeeId field — re-pointing is delete-and-recreate) — shown read-only instead of the
   * employee <Select>.
   */
  lockedEmployeeName?: string
}

export function PayrollEmployeeIdentifierForm({
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
  employees,
  lockedEmployeeName,
}: PayrollEmployeeIdentifierFormProps) {
  const form = useForm<PayrollEmployeeIdentifierFormValues>({
    resolver: zodResolver(payrollEmployeeIdentifierFormSchema),
    defaultValues: defaultValues ?? DEFAULT_PAYROLL_EMPLOYEE_IDENTIFIER_FORM_VALUES,
  })
  const errors = form.formState.errors

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this employee identifier.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PayrollEmployeeIdentifierFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-xl space-y-6">
      <div className="space-y-2">
        <Label htmlFor="pei-employeeId">Employee</Label>
        {lockedEmployeeName !== undefined ? (
          <p id="pei-employeeId" className="text-sm">
            {lockedEmployeeName}
          </p>
        ) : (
          <>
            <Select id="pei-employeeId" autoFocus aria-invalid={errors.employeeId !== undefined} {...form.register('employeeId')}>
              <option value="">Select an employee…</option>
              {employees?.map((employee) => (
                <option key={employee.id} value={employee.id}>
                  {employee.firstName} {employee.lastName}
                </option>
              ))}
            </Select>
            {errors.employeeId && <p className="text-sm text-destructive">{errors.employeeId.message}</p>}
          </>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="pei-externalEmployeeId">External employee id</Label>
        <Input
          id="pei-externalEmployeeId"
          autoFocus={lockedEmployeeName !== undefined}
          placeholder="ADP-00123"
          aria-invalid={errors.externalEmployeeId !== undefined}
          {...form.register('externalEmployeeId')}
        />
        {errors.externalEmployeeId && (
          <p className="text-sm text-destructive">{errors.externalEmployeeId.message}</p>
        )}
      </div>

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
