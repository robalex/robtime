import { useMutation } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type DifferentialSandboxRequest = components['schemas']['DifferentialSandboxRequest']
export type DifferentialSandboxResponse = components['schemas']['DifferentialSandboxResponse']
export type DifferentialZone = components['schemas']['DifferentialZoneResponse']
export type SandboxTestPunch = components['schemas']['SandboxTestPunch']
export type DifferentialOutcome = components['schemas']['DifferentialOutcome']
export type DifferentialEvaluation = components['schemas']['DifferentialEvaluationResponse']
export type ShiftDifferentialExplanation = components['schemas']['ShiftDifferentialExplanationResponse']
export type QualifyingSegment = components['schemas']['QualifyingSegmentResponse']

// Not a query: the sandbox's inputs (employee, pay rule, window) are picked live in the UI and
// re-run on demand — same "mutation as a live compute" reasoning as usePreviewTimecard and
// useRunPayRuleWhatIf, neither of which has a stable cache key worth querying against.
export function useDifferentialSandbox() {
  return useMutation({
    mutationFn: async (request: DifferentialSandboxRequest) => {
      const { data, error } = await api.POST('/differentials/sandbox', { body: request })
      if (error) {
        throw error
      }
      return data
    },
  })
}
