import { useForm } from 'react-hook-form'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select } from '@/components/ui/select'
import type { PremiumRuleMetadata } from '@/features/premiumRules/queries'
import {
  clientPremiumPolicyFormSchema,
  DEFAULT_CLIENT_PREMIUM_POLICY_FORM_VALUES,
  WAIVER_POLICIES,
  WAIVER_POLICY_LABELS,
  type ClientPremiumPolicyFormValues,
} from './formSchema'

export type { ClientPremiumPolicyFormValues } from './formSchema'

interface ClientPremiumPolicyFormProps {
  premiumRules: PremiumRuleMetadata[]
  defaultValues?: ClientPremiumPolicyFormValues
  submitLabel: string
  onSubmit: (values: ClientPremiumPolicyFormValues) => Promise<unknown>
  onCancel: () => void
}

export function ClientPremiumPolicyForm({
  premiumRules,
  defaultValues,
  submitLabel,
  onSubmit,
  onCancel,
}: ClientPremiumPolicyFormProps) {
  const form = useForm<ClientPremiumPolicyFormValues>({
    resolver: zodResolver(clientPremiumPolicyFormSchema),
    defaultValues: defaultValues ?? DEFAULT_CLIENT_PREMIUM_POLICY_FORM_VALUES,
  })
  const values = form.watch()
  const errors = form.formState.errors

  const selectedRule = premiumRules.find((rule) => rule.code === values.premiumCode)

  const submit = form.handleSubmit(async (submitted) => {
    try {
      await onSubmit(submitted)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this waiver policy.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in submitted) {
          form.setError(field as keyof ClientPremiumPolicyFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  return (
    <form onSubmit={submit} className="max-w-2xl space-y-8">
      <div className="space-y-2">
        <Label htmlFor="cpp-premiumCode">Premium</Label>
        <Select id="cpp-premiumCode" autoFocus aria-invalid={errors.premiumCode !== undefined} {...form.register('premiumCode')}>
          <option value="">Select a premium…</option>
          {premiumRules.map((rule) => (
            <option key={rule.code} value={rule.code}>
              {rule.name} ({rule.jurisdiction})
            </option>
          ))}
        </Select>
        {errors.premiumCode && <p className="text-sm text-destructive">{errors.premiumCode.message}</p>}
        {selectedRule && (
          <p className="text-xs text-muted-foreground">
            {selectedRule.description} Built-in default:{' '}
            <span className="font-medium">{WAIVER_POLICY_LABELS[selectedRule.waiverPolicy]}</span>.
          </p>
        )}
      </div>

      <div className="space-y-2">
        <Label htmlFor="cpp-waiverPolicy">This client's waiver policy</Label>
        <Select id="cpp-waiverPolicy" {...form.register('waiverPolicy')}>
          {WAIVER_POLICIES.map((policy) => (
            <option key={policy} value={policy}>
              {WAIVER_POLICY_LABELS[policy]}
            </option>
          ))}
        </Select>
        <p className="text-xs text-muted-foreground">
          Overrides the premium's built-in default for this client from the effective date below,
          until removed or superseded by a later policy for the same premium.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="space-y-2">
          <Label htmlFor="cpp-effectiveFrom">Effective from</Label>
          <Input
            id="cpp-effectiveFrom"
            type="date"
            aria-invalid={errors.effectiveFrom !== undefined}
            {...form.register('effectiveFrom')}
          />
          {errors.effectiveFrom && <p className="text-sm text-destructive">{errors.effectiveFrom.message}</p>}
        </div>
        <div className="space-y-2">
          <Label htmlFor="cpp-effectiveTo">Effective to</Label>
          <Input
            id="cpp-effectiveTo"
            type="date"
            aria-invalid={errors.effectiveTo !== undefined}
            {...form.register('effectiveTo')}
          />
          <p className="text-xs text-muted-foreground">Leave blank for still in effect.</p>
          {errors.effectiveTo && <p className="text-sm text-destructive">{errors.effectiveTo.message}</p>}
        </div>
      </div>

      <div className="space-y-2">
        <Label htmlFor="cpp-justification">Justification</Label>
        <Input id="cpp-justification" placeholder="Optional — e.g. a citation or sign-off reference" {...form.register('justification')} />
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
