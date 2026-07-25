import type { ComponentProps } from 'react'
import { useForm } from 'react-hook-form'
import { z } from 'zod'
import { zodResolver } from '@/lib/zodResolver'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

// Mirrors the server's EmployeeRequestValidator; the server stays authoritative. Optional fields are
// `.optional()` rather than defaulted, so a blank input sends nothing and the server applies its own
// default — the discipline §6 Rule 2 asks for ("show the default as placeholder, send null, let the
// server decide") rather than the UI inventing a second source of truth for defaults.
const employeeFormSchema = z.object({
  firstName: z.string().trim().min(1, 'First name is required.'),
  lastName: z.string().trim().min(1, 'Last name is required.'),
  minimumWage: z.coerce
    .number({ message: 'Minimum wage must be a number.' })
    .nonnegative('Minimum wage cannot be negative.'),
  state: z.string().trim().optional(),
  homeTimeZoneId: z.string().trim().optional(),
  middleName: z.string().trim().optional(),
  salutation: z.string().trim().optional(),
  postNominalLetters: z.string().trim().optional(),
})

export type EmployeeFormValues = z.infer<typeof employeeFormSchema>

interface EmployeeFormProps {
  defaultValues?: Partial<EmployeeFormValues>
  submitLabel: string
  onSubmit: (values: EmployeeFormValues) => Promise<unknown>
  onCancel: () => void
}

export function EmployeeForm({ defaultValues, submitLabel, onSubmit, onCancel }: EmployeeFormProps) {
  const form = useForm<EmployeeFormValues>({
    resolver: zodResolver(employeeFormSchema),
    defaultValues: {
      firstName: '',
      lastName: '',
      minimumWage: 0,
      state: '',
      homeTimeZoneId: '',
      middleName: '',
      salutation: '',
      postNominalLetters: '',
      ...defaultValues,
    },
  })

  const submit = form.handleSubmit(async (values) => {
    try {
      await onSubmit(values)
    } catch (error) {
      const problem = toApiProblem(error, 'Could not save this employee.')
      const fields = Object.entries(problem.fieldErrors)
      for (const [field, message] of fields) {
        if (field in values) {
          form.setError(field as keyof EmployeeFormValues, { message })
        }
      }
      if (fields.length === 0) {
        form.setError('root', { message: problem.message })
      }
    }
  })

  const field = (name: keyof EmployeeFormValues) => form.formState.errors[name]

  return (
    <form onSubmit={submit} className="max-w-xl space-y-8">
      {/* Essential — always visible (§6 Rule 2). */}
      <div className="grid gap-4 sm:grid-cols-2">
        <Field label="First name" name="firstName" form={form} error={field('firstName')} autoFocus />
        <Field label="Last name" name="lastName" form={form} error={field('lastName')} />
        <Field
          label="Minimum wage"
          name="minimumWage"
          form={form}
          error={field('minimumWage')}
          type="number"
          step="0.01"
          min="0"
        />
      </div>

      {/* Common — visible, grouped. */}
      <fieldset className="grid gap-4 sm:grid-cols-2">
        <legend className="mb-2 text-sm font-medium">Location</legend>
        <Field label="State" name="state" form={form} error={field('state')} placeholder="CA" />
        <Field
          label="Home time zone"
          name="homeTimeZoneId"
          form={form}
          error={field('homeTimeZoneId')}
          // Placeholder, not a value: leaving it blank sends nothing and the server applies its own
          // default rather than the UI hard-coding a second copy of it.
          placeholder="America/New_York"
        />
      </fieldset>

      {/* Advanced — collapsed (§6 Rule 2). Native details/summary: keyboard- and
          screen-reader-accessible with no JavaScript and no extra dependency. */}
      <details className="rounded-lg border p-4">
        <summary className="cursor-pointer text-sm font-medium">Additional name details</summary>
        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <Field label="Salutation" name="salutation" form={form} error={field('salutation')} placeholder="Dr." />
          <Field label="Middle name" name="middleName" form={form} error={field('middleName')} />
          <Field
            label="Post-nominal letters"
            name="postNominalLetters"
            form={form}
            error={field('postNominalLetters')}
            placeholder="PhD"
          />
        </div>
      </details>

      {form.formState.errors.root && (
        <p className="text-sm text-destructive">{form.formState.errors.root.message}</p>
      )}

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

// `name` and `form` are both native input attributes, so they're omitted before being redeclared —
// `name` narrows to this form's fields, and `form` here means the RHF instance, not the HTML
// form-association attribute.
interface FieldProps extends Omit<ComponentProps<'input'>, 'name' | 'form'> {
  label: string
  name: keyof EmployeeFormValues
  form: ReturnType<typeof useForm<EmployeeFormValues>>
  error?: { message?: string }
}

function Field({ label, name, form, error, ...inputProps }: FieldProps) {
  const id = `employee-${name}`
  return (
    <div className="space-y-2">
      <Label htmlFor={id}>{label}</Label>
      <Input
        id={id}
        aria-invalid={error !== undefined}
        aria-describedby={error ? `${id}-error` : undefined}
        {...form.register(name)}
        {...inputProps}
      />
      {error && (
        <p id={`${id}-error`} className="text-sm text-destructive">
          {error.message}
        </p>
      )}
    </div>
  )
}
