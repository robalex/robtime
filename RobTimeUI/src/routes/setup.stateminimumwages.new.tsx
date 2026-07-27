import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { LocalDate } from '@js-joda/core'
import { StateMinimumWageForm } from '@/features/stateMinimumWages/StateMinimumWageForm'
import { useCreateStateMinimumWage } from '@/features/stateMinimumWages/queries'

export const Route = createFileRoute('/setup/stateminimumwages/new')({
  component: NewStateMinimumWage,
})

function NewStateMinimumWage() {
  const navigate = useNavigate()
  const createStateMinimumWage = useCreateStateMinimumWage()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New state minimum wage rate</h1>
      <StateMinimumWageForm
        submitLabel="Create rate"
        onCancel={() => void navigate({ to: '/setup/stateminimumwages' })}
        onSubmit={async (values) => {
          // LocalDate.parse round-trips the <input type="date"> string through js-joda purely to
          // validate it's a real calendar date before the request goes out — the wire value is the
          // same ISO string either way.
          await createStateMinimumWage.mutateAsync({
            state: values.state,
            effectiveFrom: LocalDate.parse(values.effectiveFrom).toString(),
            effectiveTo: values.effectiveTo ? LocalDate.parse(values.effectiveTo).toString() : undefined,
            amount: values.amount,
          })
          await navigate({ to: '/setup/stateminimumwages' })
        }}
      />
    </div>
  )
}
