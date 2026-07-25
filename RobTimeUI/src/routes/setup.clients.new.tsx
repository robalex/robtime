import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useQueryClient } from '@tanstack/react-query'
import { ClientForm } from '@/features/clients/ClientForm'
import { useCreateClient } from '@/features/clients/queries'
import { setSelectedClientId } from '@/auth/clientSelection'

export const Route = createFileRoute('/setup/clients/new')({
  component: NewClient,
})

function NewClient() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const createClient = useCreateClient()

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold tracking-tight">New client</h1>
      <ClientForm
        submitLabel="Create client"
        onCancel={() => void navigate({ to: '/setup/clients', search: {} })}
        onSubmit={async (values) => {
          const created = await createClient.mutateAsync({ name: values.name })

          // Scope into the client that was just created. Creating one is the first step of
          // configuring it, so a SystemAdmin is about to work inside it — without this they'd land
          // on its page still scoped elsewhere, and everything they added next would be filtered
          // away. Harmless for other roles: the API ignores the selection header for them.
          setSelectedClientId(created.id)
          await queryClient.invalidateQueries()

          await navigate({
            to: '/setup/clients/$clientId',
            params: { clientId: String(created.id) },
          })
        }}
      />
    </div>
  )
}
