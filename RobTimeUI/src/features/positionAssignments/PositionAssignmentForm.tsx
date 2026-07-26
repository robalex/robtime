import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import type { Position } from '@/features/positions/queries'

// Mirrors PositionAssignmentValidator.ValidateShape; the server stays authoritative (including the
// overlap check, which needs the other assignments and can't be done client-side without a round
// trip that would just be stale by the time it mattered).
const assignmentFormSchema = z
  .object({
    positionId: z.string().min(1, 'Choose a position.'),
    effectiveFrom: z.string().min(1, 'Effective date is required.'),
    effectiveTo: z.string().optional(),
    rate: z.string().optional(),
  })
  .refine((data) => !data.effectiveTo || data.effectiveTo >= data.effectiveFrom, {
    message: 'The end date cannot be before the start date.',
    path: ['effectiveTo'],
  })
  .refine((data) => !data.rate || Number(data.rate) >= 0, {
    message: 'Rate cannot be negative.',
    path: ['rate'],
  })

export type PositionAssignmentFormValues = z.infer<typeof assignmentFormSchema>

interface PositionAssignmentFormProps {
  positions: Position[]
  defaultValues?: PositionAssignmentFormValues
  submitLabel: string
  onSubmit: (values: PositionAssignmentFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PositionAssignmentForm({
  positions,
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: PositionAssignmentFormProps) {
  const form = useForm<PositionAssignmentFormValues>({
    resolver: zodResolver(assignmentFormSchema),
    defaultValues: defaultValues ?? { positionId: '', effectiveFrom: '', effectiveTo: '', rate: '' },
  })

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this assignment.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PositionAssignmentFormValues, { message })
        }
      }
      // A 409 overlap conflict has no field to attach to (PositionAssignmentValidator.FindConflict
      // reports against the whole proposed range, not one input) — surfaces as the root message.
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  const errors = form.formState.errors

  return (
    <form onSubmit={submit} className="max-w-md space-y-6">
      <div className="space-y-2">
        <Label htmlFor="assignment-positionId">Position</Label>
        <Select
          id="assignment-positionId"
          autoFocus
          aria-invalid={errors.positionId !== undefined}
          {...form.register('positionId')}
        >
          <option value="">Choose a position…</option>
          {positions.map((position) => (
            <option key={position.id} value={position.id}>
              {position.code} — {position.name}
            </option>
          ))}
        </Select>
        {errors.positionId && <p className="text-sm text-destructive">{errors.positionId.message}</p>}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="space-y-2">
          <Label htmlFor="assignment-effectiveFrom">Effective from</Label>
          <Input
            id="assignment-effectiveFrom"
            type="date"
            aria-invalid={errors.effectiveFrom !== undefined}
            {...form.register('effectiveFrom')}
          />
          {errors.effectiveFrom && <p className="text-sm text-destructive">{errors.effectiveFrom.message}</p>}
        </div>

        <div className="space-y-2">
          <Label htmlFor="assignment-effectiveTo">Effective to</Label>
          <Input
            id="assignment-effectiveTo"
            type="date"
            aria-invalid={errors.effectiveTo !== undefined}
            {...form.register('effectiveTo')}
          />
          <p className="text-xs text-muted-foreground">Leave blank if still in effect.</p>
          {errors.effectiveTo && <p className="text-sm text-destructive">{errors.effectiveTo.message}</p>}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="assignment-rate">Rate override</Label>
        <Input
          id="assignment-rate"
          type="number"
          step="0.01"
          min="0"
          placeholder="Position's base rate"
          aria-invalid={errors.rate !== undefined}
          {...form.register('rate')}
        />
        <p className="text-xs text-muted-foreground">
          Leave blank to use the position's base rate for this employee.
        </p>
        {errors.rate && <p className="text-sm text-destructive">{errors.rate.message}</p>}
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
