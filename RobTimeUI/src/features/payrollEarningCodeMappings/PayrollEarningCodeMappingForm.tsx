import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import {
  DEFAULT_PAYROLL_EARNING_CODE_MAPPING_FORM_VALUES,
  LINE_TYPE_LABELS,
  LINE_TYPES,
  payrollEarningCodeMappingFormSchema,
  VALUE_BASES,
  VALUE_BASIS_LABELS,
  type PayrollEarningCodeMappingFormValues,
} from './formSchema'

export type { PayrollEarningCodeMappingFormValues } from './formSchema'

interface PayrollEarningCodeMappingFormProps {
  defaultValues?: PayrollEarningCodeMappingFormValues
  submitLabel: string
  onSubmit: (values: PayrollEarningCodeMappingFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PayrollEarningCodeMappingForm({
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: PayrollEarningCodeMappingFormProps) {
  const form = useForm<PayrollEarningCodeMappingFormValues>({
    resolver: zodResolver(payrollEarningCodeMappingFormSchema),
    defaultValues: defaultValues ?? DEFAULT_PAYROLL_EARNING_CODE_MAPPING_FORM_VALUES,
  })
  const errors = form.formState.errors

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this earning-code mapping.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PayrollEarningCodeMappingFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-2xl space-y-6">
      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="pecm-lineType">Line type</Label>
          <Select id="pecm-lineType" autoFocus {...form.register('lineType')}>
            {LINE_TYPES.map((lineType) => (
              <option key={lineType} value={lineType}>
                {LINE_TYPE_LABELS[lineType]}
              </option>
            ))}
          </Select>
        </div>
        <div className="space-y-2">
          <Label htmlFor="pecm-lineCode">Line code</Label>
          <Input
            id="pecm-lineCode"
            placeholder="e.g. OVERTIME, or blank for Regular"
            aria-invalid={errors.lineCode !== undefined}
            {...form.register('lineCode')}
          />
          <p className="text-xs text-muted-foreground">
            Blank for Regular; OVERTIME or DOUBLETIME for Overtime premium; a real bonus, differential,
            or premium code for the rest — the server checks it against your configured rules.
          </p>
          {errors.lineCode && <p className="text-sm text-destructive">{errors.lineCode.message}</p>}
        </div>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="pecm-earningCode">Earning code</Label>
          <Input
            id="pecm-earningCode"
            placeholder="REG"
            aria-invalid={errors.earningCode !== undefined}
            {...form.register('earningCode')}
          />
          {errors.earningCode && <p className="text-sm text-destructive">{errors.earningCode.message}</p>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="pecm-valueBasis">Value basis</Label>
          <Select id="pecm-valueBasis" aria-invalid={errors.valueBasis !== undefined} {...form.register('valueBasis')}>
            {VALUE_BASES.map((basis) => (
              <option key={basis} value={basis}>
                {VALUE_BASIS_LABELS[basis]}
              </option>
            ))}
          </Select>
          {errors.valueBasis && <p className="text-sm text-destructive">{errors.valueBasis.message}</p>}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="pecm-description">Description</Label>
        <Input id="pecm-description" placeholder="Optional" {...form.register('description')} />
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
