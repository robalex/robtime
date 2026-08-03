import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import {
  DEFAULT_PAYROLL_EXPORT_PROFILE_FORM_VALUES,
  GROUPING_LABELS,
  GROUPINGS,
  payrollExportProfileFormSchema,
  PROVIDER_LABELS,
  PROVIDERS,
  ROUNDING_POLICIES,
  ROUNDING_POLICY_LABELS,
  type PayrollExportProfileFormValues,
} from './formSchema'

export type { PayrollExportProfileFormValues } from './formSchema'

interface PayrollExportProfileFormProps {
  defaultValues?: PayrollExportProfileFormValues
  submitLabel: string
  onSubmit: (values: PayrollExportProfileFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PayrollExportProfileForm({
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: PayrollExportProfileFormProps) {
  const form = useForm<PayrollExportProfileFormValues>({
    resolver: zodResolver(payrollExportProfileFormSchema),
    defaultValues: defaultValues ?? DEFAULT_PAYROLL_EXPORT_PROFILE_FORM_VALUES,
  })
  const errors = form.formState.errors
  const roundingPolicy = form.watch('roundingPolicy')

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this payroll export profile.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PayrollExportProfileFormValues, { message })
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
          <Label htmlFor="pep-name">Name</Label>
          <Input
            id="pep-name"
            autoFocus
            placeholder="ADP Workforce Now"
            aria-invalid={errors.name !== undefined}
            {...form.register('name')}
          />
          {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="pep-provider">Provider</Label>
          <Select id="pep-provider" {...form.register('provider')}>
            {PROVIDERS.map((provider) => (
              <option key={provider} value={provider}>
                {PROVIDER_LABELS[provider]}
              </option>
            ))}
          </Select>
        </div>
      </div>

      <details className="rounded-lg border p-4">
        <summary className="cursor-pointer text-sm font-medium">Advanced settings</summary>
        <div className="mt-4 space-y-6">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="pep-grouping">Row grouping</Label>
              <Select id="pep-grouping" {...form.register('grouping')}>
                {GROUPINGS.map((grouping) => (
                  <option key={grouping} value={grouping}>
                    {GROUPING_LABELS[grouping]}
                  </option>
                ))}
              </Select>
            </div>
            <div className="space-y-2">
              <Label htmlFor="pep-roundingPolicy">Rounding policy</Label>
              <Select id="pep-roundingPolicy" {...form.register('roundingPolicy')}>
                {ROUNDING_POLICIES.map((policy) => (
                  <option key={policy} value={policy}>
                    {ROUNDING_POLICY_LABELS[policy]}
                  </option>
                ))}
              </Select>
            </div>
          </div>

          {roundingPolicy === 'AdjustmentRow' && (
            <div className="space-y-2">
              <Label htmlFor="pep-adjustmentEarningCode">Adjustment earning code</Label>
              <Input
                id="pep-adjustmentEarningCode"
                placeholder="ADJ"
                aria-invalid={errors.adjustmentEarningCode !== undefined}
                {...form.register('adjustmentEarningCode')}
              />
              {errors.adjustmentEarningCode && (
                <p className="text-sm text-destructive">{errors.adjustmentEarningCode.message}</p>
              )}
            </div>
          )}

          <div className="grid gap-4 sm:grid-cols-2">
            <div className="space-y-2">
              <Label htmlFor="pep-amountScale">Amount decimal places</Label>
              <Input
                id="pep-amountScale"
                type="number"
                min="0"
                aria-invalid={errors.amountScale !== undefined}
                {...form.register('amountScale')}
              />
              {errors.amountScale && <p className="text-sm text-destructive">{errors.amountScale.message}</p>}
            </div>
            <div className="space-y-2">
              <Label htmlFor="pep-hoursScale">Hours decimal places</Label>
              <Input
                id="pep-hoursScale"
                type="number"
                min="0"
                aria-invalid={errors.hoursScale !== undefined}
                {...form.register('hoursScale')}
              />
              {errors.hoursScale && <p className="text-sm text-destructive">{errors.hoursScale.message}</p>}
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
