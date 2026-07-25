import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

// Mirrors the server's ClientRequestValidator ("Name is required.") rather than replacing it. The
// server stays authoritative — this exists so the user gets the answer on blur instead of after a
// round trip, and the messages here are UX copy, not the contract (UI_PLAN.md §3).
const clientFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required.'),
})

export type ClientFormValues = z.infer<typeof clientFormSchema>

interface ClientFormProps {
  defaultValues?: ClientFormValues
  submitLabel: string
  onSubmit: (values: ClientFormValues) => Promise<unknown>
  onCancel: () => void
}

export function ClientForm({ defaultValues, submitLabel, onSubmit, onCancel }: ClientFormProps) {
  const form = useForm<ClientFormValues>({
    resolver: zodResolver(clientFormSchema),
    defaultValues: defaultValues ?? { name: '' },
  })

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      // Server-side validation lands on the matching field, so a rule the client doesn't know about
      // still reads as a normal field error rather than a detached banner. Anything without a field
      // (conflict, not-found, a 500) goes to the form-level `root` error instead of being swallowed.
      const problem = toApiProblem(error, 'Could not save this client.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof ClientFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  const nameError = form.formState.errors.name
  const rootError = form.formState.errors.root

  return (
    <form onSubmit={submit} className="max-w-md space-y-6">
      <div className="space-y-2">
        <Label htmlFor="name">Name</Label>
        <Input
          id="name"
          autoFocus
          aria-invalid={nameError !== undefined}
          aria-describedby={nameError ? 'name-error' : undefined}
          {...form.register('name')}
        />
        {nameError && (
          <p id="name-error" className="text-sm text-destructive">
            {nameError.message}
          </p>
        )}
      </div>

      {rootError && <p className="text-sm text-destructive">{rootError.message}</p>}

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
