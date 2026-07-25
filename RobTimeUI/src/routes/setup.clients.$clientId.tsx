import { useState } from 'react'
import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useClient, useDeleteClient, useUpdateClient } from '@/features/clients/queries'
import { ClientForm } from '@/features/clients/ClientForm'
import { toApiProblem } from '@/lib/problem'
import { Button } from '@/components/ui/button'

export const Route = createFileRoute('/setup/clients/$clientId')({
  component: EditClient,
})

function EditClient() {
  const { clientId } = Route.useParams()
  const id = Number(clientId)
  const navigate = useNavigate()

  const { data: client, isPending, isError, error } = useClient(id)
  const updateClient = useUpdateClient(id)
  const deleteClient = useDeleteClient()
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [confirmingDelete, setConfirmingDelete] = useState(false)

  if (isPending) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (isError) {
    const problem = toApiProblem(error, 'Could not load this client.')
    return (
      <div className="space-y-4">
        <h1 className="text-xl font-semibold">
          {problem.status === 404 ? 'Client not found' : 'Could not load this client'}
        </h1>
        <p className="text-sm text-muted-foreground">{problem.message}</p>
        <Link to="/setup/clients" search={{}} className="text-sm underline underline-offset-4">
          Back to clients
        </Link>
      </div>
    )
  }

  return (
    <div className="space-y-8">
      <div className="space-y-1">
        <Link to="/setup/clients" search={{}} className="text-sm text-muted-foreground underline-offset-4 hover:underline">
          ← Clients
        </Link>
        <h1 className="text-2xl font-semibold tracking-tight">{client.name}</h1>
        <p className="text-sm text-muted-foreground">
          Created by {client.createdBy} on {new Date(client.createdDate).toLocaleDateString()}
        </p>
      </div>

      <ClientForm
        defaultValues={{ name: client.name }}
        submitLabel="Save changes"
        onCancel={() => void navigate({ to: '/setup/clients', search: {} })}
        onSubmit={(values) => updateClient.mutateAsync({ name: values.name })}
      />

      <div className="space-y-3 border-t pt-6">
        <div className="space-y-1">
          <h2 className="text-sm font-medium">Delete this client</h2>
          <p className="text-sm text-muted-foreground">
            The client is soft-deleted and hidden everywhere, but its payroll history is retained.
          </p>
        </div>

        {/* Inline confirmation rather than a modal: it keeps the destructive action on its own
            route with a visible URL, matching the same reasoning §6 gives for effective-dated
            edits — a modal invites skimming past the thing that matters. */}
        {confirmingDelete ? (
          <div className="flex items-center gap-2">
            <Button
              variant="destructive"
              size="sm"
              disabled={deleteClient.isPending}
              onClick={async () => {
                setDeleteError(null)
                try {
                  await deleteClient.mutateAsync(id)
                  await navigate({ to: '/setup/clients', search: {} })
                } catch (err) {
                  setDeleteError(toApiProblem(err, 'Could not delete this client.').message)
                }
              }}
            >
              {deleteClient.isPending ? 'Deleting…' : 'Yes, delete'}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(false)}>
              Keep it
            </Button>
          </div>
        ) : (
          <Button variant="outline" size="sm" onClick={() => setConfirmingDelete(true)}>
            Delete client
          </Button>
        )}

        {deleteError && <p className="text-sm text-destructive">{deleteError}</p>}
      </div>
    </div>
  )
}
