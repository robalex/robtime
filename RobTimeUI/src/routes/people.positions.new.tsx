import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { PositionForm } from '@/features/positions/PositionForm'
import { useCreatePosition } from '@/features/positions/queries'
import { RequiresClient } from '@/components/RequiresClient'

export const Route = createFileRoute('/people/positions/new')({
  component: NewPosition,
})

function NewPosition() {
  return <RequiresClient>{(clientId) => <NewPositionForm clientId={clientId} />}</RequiresClient>
}

function NewPositionForm({ clientId }: { clientId: number }) {
  const navigate = useNavigate()
  const createPosition = useCreatePosition()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New position</h1>
      <PositionForm
        submitLabel="Create position"
        onCancel={() => void navigate({ to: '/people/positions' })}
        onSubmit={async (values) => {
          await createPosition.mutateAsync({ clientId, ...values })
          await navigate({ to: '/people/positions' })
        }}
      />
    </div>
  )
}
