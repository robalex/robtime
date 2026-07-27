import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import {
  ClientPremiumPolicyForm,
  type ClientPremiumPolicyFormValues,
} from '@/features/clientPremiumPolicies/ClientPremiumPolicyForm'
import { DEFAULT_CLIENT_PREMIUM_POLICY_FORM_VALUES } from '@/features/clientPremiumPolicies/formSchema'
import { useCreateClientPremiumPolicy } from '@/features/clientPremiumPolicies/queries'
import { usePremiumRules } from '@/features/premiumRules/queries'
import { RequiresClient } from '@/components/RequiresClient'
import { todayLocalDate, toWireLocalDate } from '@/lib/dates'

export const Route = createFileRoute('/setup/clientpremiumpolicies/new')({
  component: NewClientPremiumPolicy,
})

function NewClientPremiumPolicy() {
  return <RequiresClient>{(clientId) => <NewClientPremiumPolicyForm clientId={clientId} />}</RequiresClient>
}

function toRequestBody(clientId: number, values: ClientPremiumPolicyFormValues) {
  return {
    clientId,
    premiumCode: values.premiumCode,
    waiverPolicy: values.waiverPolicy,
    effectiveFrom: values.effectiveFrom,
    effectiveTo: values.effectiveTo || undefined,
    justification: values.justification || undefined,
  }
}

function NewClientPremiumPolicyForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const { data: premiumRules, isPending, isError } = usePremiumRules()
  const createPolicy = useCreateClientPremiumPolicy()

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Link
          to="/setup/clientpremiumpolicies"
          className="text-sm text-muted-foreground underline-offset-4 hover:underline"
        >
          ← Waiver policies
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">New waiver policy</h1>
      </div>

      {isPending && <p className="text-sm text-muted-foreground">Loading…</p>}
      {isError && <p className="text-sm text-destructive">Could not load the premium registry.</p>}

      {premiumRules && (
        <ClientPremiumPolicyForm
          premiumRules={premiumRules}
          defaultValues={{
            ...DEFAULT_CLIENT_PREMIUM_POLICY_FORM_VALUES,
            effectiveFrom: toWireLocalDate(todayLocalDate()),
          }}
          submitLabel="Create waiver policy"
          onCancel={() => void navigate({ to: '/setup/clientpremiumpolicies' })}
          onSubmit={async (values) => {
            await createPolicy.mutateAsync(toRequestBody(clientId, values))
            await navigate({ to: '/setup/clientpremiumpolicies' })
          }}
        />
      )}
    </div>
  )
}
