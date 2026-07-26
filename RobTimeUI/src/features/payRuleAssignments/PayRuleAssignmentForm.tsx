import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { cn } from '@/lib/utils'
import type { PayRule } from '@/features/payRules/queries'

// Mirrors PayRuleAssignmentValidator.ValidateShape; the server stays authoritative (including the
// overlap check, which needs the other assignments and can't be done client-side without a round
// trip that would just be stale by the time it mattered).
const assignmentFormSchema = z
  .object({
    payRuleId: z.string().min(1, 'Choose a pay rule.'),
    effectiveFrom: z.string().min(1, 'Effective date is required.'),
    effectiveTo: z.string().optional(),
  })
  .refine((data) => !data.effectiveTo || data.effectiveTo >= data.effectiveFrom, {
    message: 'The end date cannot be before the start date.',
    path: ['effectiveTo'],
  })

export type PayRuleAssignmentFormValues = z.infer<typeof assignmentFormSchema>

interface PayRuleAssignmentFormProps {
  payRules: PayRule[]
  defaultValues?: PayRuleAssignmentFormValues
  submitLabel: string
  onSubmit: (values: PayRuleAssignmentFormValues) => Promise<unknown>
  onCancel: () => void
}

export function PayRuleAssignmentForm({
  payRules,
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: PayRuleAssignmentFormProps) {
  const form = useForm<PayRuleAssignmentFormValues>({
    resolver: zodResolver(assignmentFormSchema),
    defaultValues: defaultValues ?? { payRuleId: '', effectiveFrom: '', effectiveTo: '' },
  })

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this assignment.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof PayRuleAssignmentFormValues, { message })
        }
      }
      // A 409 overlap conflict has no field to attach to (PayRuleAssignmentValidator.FindConflict
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
        <Label htmlFor="assignment-payRuleId">Pay rule</Label>
        <select
          id="assignment-payRuleId"
          autoFocus
          aria-invalid={errors.payRuleId !== undefined}
          className={cn(
            'flex h-9 w-full rounded-md border border-input bg-transparent px-3 py-1 text-sm shadow-sm transition-colors',
            'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring',
            'disabled:cursor-not-allowed disabled:opacity-50',
            'aria-invalid:border-destructive aria-invalid:focus-visible:ring-destructive',
          )}
          {...form.register('payRuleId')}
        >
          <option value="">Choose a pay rule…</option>
          {payRules.map((payRule) => (
            <option key={payRule.id} value={payRule.id}>
              {payRule.name} ({payRule.status})
            </option>
          ))}
        </select>
        {errors.payRuleId && <p className="text-sm text-destructive">{errors.payRuleId.message}</p>}
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
