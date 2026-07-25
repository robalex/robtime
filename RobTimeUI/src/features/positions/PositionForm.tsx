import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

// Mirrors PositionRequestValidator; the server stays authoritative.
const positionFormSchema = z.object({
  code: z.string().trim().min(1, 'Code is required.'),
  name: z.string().trim().min(1, 'Name is required.'),
  baseRate: z.coerce
    .number({ message: 'Base rate must be a number.' })
    .nonnegative('Base rate cannot be negative.'),
})

export type PositionFormValues = z.infer<typeof positionFormSchema>

interface PositionFormProps {
  defaultValues?: PositionFormValues
  submitLabel: string
  onSubmit: (values: PositionFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PositionForm({ defaultValues, submitLabel, onSubmit, onCancel }: PositionFormProps) {
  const form = useForm<PositionFormValues>({
    resolver: zodResolver(positionFormSchema),
    defaultValues: defaultValues ?? { code: '', name: '', baseRate: 0 },
  })

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this position.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PositionFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  const errors = form.formState.errors

  return (
    <form onSubmit={submit} className="max-w-md space-y-6">
      <div className="space-y-2">
        <Label htmlFor="position-code">Code</Label>
        <Input
          id="position-code"
          autoFocus
          placeholder="COOK"
          aria-invalid={errors.code !== undefined}
          {...form.register('code')}
        />
        {errors.code && <p className="text-sm text-destructive">{errors.code.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="position-name">Name</Label>
        <Input
          id="position-name"
          placeholder="Cook"
          aria-invalid={errors.name !== undefined}
          {...form.register('name')}
        />
        {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
      </div>

      <div className="space-y-2">
        <Label htmlFor="position-baseRate">Base rate</Label>
        <Input
          id="position-baseRate"
          type="number"
          step="0.01"
          min="0"
          aria-invalid={errors.baseRate !== undefined}
          {...form.register('baseRate')}
        />
        <p className="text-xs text-muted-foreground">
          The default hourly rate for this position. An individual assignment can override it.
        </p>
        {errors.baseRate && <p className="text-sm text-destructive">{errors.baseRate.message}</p>}
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
