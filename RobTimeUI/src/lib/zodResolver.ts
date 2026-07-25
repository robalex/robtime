import type { FieldValues, Resolver } from 'react-hook-form'
import type { ZodType } from 'zod'

/**
 * Minimal React Hook Form resolver for Zod.
 *
 * Hand-written rather than pulling in `@hookform/resolvers`, whose optional-peer graph
 * (`@typeschema/*` → an old `valibot`) doesn't resolve against this tree without
 * `--legacy-peer-deps` — a blunt flag that would silence genuine conflicts later too. The resolver
 * contract is small and stable enough that ~20 honest lines beat suppressing dependency resolution
 * for the whole project.
 */
export function zodResolver<TFieldValues extends FieldValues>(
  schema: ZodType<TFieldValues>,
): Resolver<TFieldValues> {
  return async (values) => {
    const result = await schema.safeParseAsync(values)

    if (result.success) {
      return { values: result.data, errors: {} }
    }

    const errors: Record<string, { type: string; message: string }> = {}
    for (const issue of result.error.issues) {
      const path = issue.path.join('.')
      // First issue per field wins — RHF shows one message per input, and later issues for the same
      // field are usually cascading consequences of the first.
      if (path && !errors[path]) {
        errors[path] = { type: issue.code, message: issue.message }
      }
    }

    return { values: {}, errors: errors as never }
  }
}
