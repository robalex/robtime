import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { ClientForm } from '@/features/clients/ClientForm'
import { useCreateClient } from '@/features/clients/queries'

export const Route = createFileRoute('/setup/clients/new')({
  component: NewClient,
})

function NewClient() {
  const navigate = useNavigate()
  const createClient = useCreateClient()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New client</h1>
      <ClientForm
        submitLabel="Create client"
        onCancel={() => void navigate({ to: '/setup/clients', search: {} })}
        onSubmit={async (values) => {
          const created = await createClient.mutateAsync({ name: values.name })
          // Land on the new client rather than back on the list: creating one is almost always the
          // first step of configuring it, so this is where the user was heading anyway.
          await navigate({
            to: '/setup/clients/$clientId',
            params: { clientId: String(created.id) },
          })
        }}
      />
    </div>
  )
}
