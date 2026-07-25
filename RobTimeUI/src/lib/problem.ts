import type { components } from '@/api/schema'

type ProblemDetails = components['schemas']['HttpValidationProblemDetails']

/**
 * The API speaks RFC 7807 `application/problem+json` for every failure — validation, not-found,
 * conflict, and unhandled exceptions alike (see Program.cs's AddProblemDetails). This turns one of
 * those into the two things a form actually needs: per-field errors to attach to inputs, and a
 * single human-readable message for everything else.
 *
 * Shared rather than per-feature because every editor in the app hits the same shape — this is the
 * piece the Employee/Position/PayRule screens reuse verbatim.
 */
export interface ApiProblem {
  /** Keyed by field name as the server spells it (camelCase, e.g. "name"). */
  fieldErrors: Record<string, string>
  /** Non-field message: `detail`, else `title`, else a status-derived fallback. */
  message: string
  status?: number
}

export function toApiProblem(error: unknown, fallback = 'Something went wrong.'): ApiProblem {
  const problem = error as ProblemDetails | undefined

  const fieldErrors: Record<string, string> = {}
  if (problem?.errors) {
    for (const [field, messages] of Object.entries(problem.errors)) {
      // ValidationProblemDetails gives an array per field; the first is the actionable one, and
      // showing all of them under a single input reads as noise.
      if (messages.length > 0) {
        fieldErrors[field] = messages[0]
      }
    }
  }

  return {
    fieldErrors,
    message: problem?.detail || problem?.title || fallback,
    status: typeof problem?.status === 'number' ? problem.status : undefined,
  }
}
