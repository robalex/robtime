import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  useClientPremiumPolicy,
  useDeleteClientPremiumPolicy,
  useUpdateClientPremiumPolicy,
} from '@/features/clientPremiumPolicies/queries'
import { usePremiumRules } from '@/features/premiumRules/queries'
import {
  ClientPremiumPolicyForm,
  type ClientPremiumPolicyFormValues,
} from '@/features/clientPremiumPolicies/ClientPremiumPolicyForm'
import type { ClientPremiumPolicy } from '@/features/clientPremiumPolicies/queries'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/clientpremiumpolicies/$clientPremiumPolicyId')({
  component: EditClientPremiumPolicy,
})

function toFormValues(policy: ClientPremiumPolicy): ClientPremiumPolicyFormValues {
  return {
    premiumCode: policy.premiumCode,
    waiverPolicy: policy.waiverPolicy,
    effectiveFrom: policy.effectiveFrom,
    effectiveTo: policy.effectiveTo ?? '',
    justification: policy.justification ?? '',
  }
}

function EditClientPremiumPolicy() {
  const { clientPremiumPolicyId } = Route.useParams()
  const id = Number(clientPremiumPolicyId)
  const navigate = useNavigate()

  const { data: policy, isPending, isError, error } = useClientPremiumPolicy(id)
  const { data: premiumRules } = usePremiumRules()
  const updatePolicy = useUpdateClientPremiumPolicy(id)
  const deletePolicy = useDeleteClientPremiumPolicy()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  const backLink = (
    <Link
      to="/setup/clientpremiumpolicies"
      className="text-sm text-muted-foreground underline-offset-4 hover:underline"
    >
      ← Waiver policies
    </Link>
  )

  if (isPending || !premiumRules) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this waiver policy.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Waiver policy not found' : 'Could not load this waiver policy'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        {backLink}
      </div>
    )
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        {backLink}
        <h1 className="text-2xl font-semibold tracking-tight">
          {premiumRules.find((rule) => rule.code === policy.premiumCode)?.name ?? policy.premiumCode}
        </h1>
        <p className="text-sm text-muted-foreground">
          Set by {policy.setBy} on {new Date(policy.setAt).toLocaleString()}
          {policy.justification ? ` — "${policy.justification}"` : ''}
        </p>
      </div>

      <ClientPremiumPolicyForm
        premiumRules={premiumRules}
        defaultValues={toFormValues(policy)}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/clientpremiumpolicies' })}
        onSubmit={(values) =>
          updatePolicy.mutateAsync({
            premiumCode: values.premiumCode,
            waiverPolicy: values.waiverPolicy,
            effectiveFrom: values.effectiveFrom,
            effectiveTo: values.effectiveTo || undefined,
            justification: values.justification || undefined,
          })
        }
      />

      <div className="max-w-2xl space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this waiver policy</h2>
          <p className="text-sm text-muted-foreground">
            Removing it reverts this premium to its built-in default for this client from today
            forward.
          </p>
        </div>
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deletePolicy.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deletePolicy.mutateAsync(id)
                  await navigate({ to: '/setup/clientpremiumpolicies' })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this waiver policy.').message)
                }
              }}
            >
              {deletePolicy.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete waiver policy
          </Button>
        )}
        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
