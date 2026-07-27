import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { stateMinimumWageFormSchema, type StateMinimumWageFormValues } from './formSchema'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

interface StateMinimumWageFormProps {
  defaultValues?: StateMinimumWageFormValues
  submitLabel: string
  onSubmit: (values: StateMinimumWageFormValues) => Promise<unknown>
  onCancel: () => void
}

export function StateMinimumWageForm({ defaultValues, submitLabel, onSubmit, onCancel }: StateMinimumWageFormProps) {
  const form = useForm<StateMinimumWageFormValues>({
    resolver: zodResolver(stateMinimumWageFormSchema),
    defaultValues: defaultValues ?? { state: '', effectiveFrom: '', effectiveTo: '', amount: 0 },
  })
  const errors = form.formState.errors

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this minimum wage rate.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof StateMinimumWageFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-md space-y-6">
      <div className="space-y-2">
        <Label htmlFor="wage-state">State</Label>
        <Input
          id="wage-state"
          autoFocus
          placeholder="CA"
          aria-invalid={errors.state !== undefined}
          {...form.register('state')}
        />
        {errors.state && <p className="text-sm text-destructive">{errors.state.message}</p>}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="wage-effectiveFrom">Effective from</Label>
          <Input
            id="wage-effectiveFrom"
            type="date"
            aria-invalid={errors.effectiveFrom !== undefined}
            {...form.register('effectiveFrom')}
          />
          {errors.effectiveFrom && <p className="text-sm text-destructive">{errors.effectiveFrom.message}</p>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="wage-effectiveTo">Effective to</Label>
          <Input id="wage-effectiveTo" type="date" {...form.register('effectiveTo')} />
          <p className="text-xs text-muted-foreground">Blank means still in effect.</p>
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="wage-amount">Amount ($/hr)</Label>
        <Input
          id="wage-amount"
          type="number"
          step="0.01"
          min="0"
          aria-invalid={errors.amount !== undefined}
          {...form.register('amount')}
        />
        {errors.amount && <p className="text-sm text-destructive">{errors.amount.message}</p>}
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
