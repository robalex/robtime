import { createFileRoute, Link, useNavigate } from '@tanstack/react-router'
import { useClients } from '@/features/clients/queries'
import { toApiProblem } from '@/lib/problem'
import { Button, buttonVariants } from '@/components/ui/button'
import { Input } from '@/components/ui/input'

const PAGE_SIZE = 25

export const Route = createFileRoute('/setup/clients/')({
  // Search and page live in the URL, not component state — so a filtered list is linkable, survives
  // a refresh, and works with the back button. This typed-search-param support is the specific
  // reason UI_PLAN.md §2 chose TanStack Router.
  validateSearch: (search: Record<string, unknown>): { q?: string; page?: number } => ({
    q: typeof search.q === 'string' && search.q !== '' ? search.q : undefined,
    page: typeof search.page === 'number' && search.page > 1 ? search.page : undefined,
  }),
  component: ClientList,
})

function ClientList() {
  const { q, page } = Route.useSearch()
  const navigate = useNavigate({ from: Route.fullPath })
  const currentPage = page ?? 1

  const { data, isPending, isError, error } = useClients({
    search: q,
    page: currentPage,
    pageSize: PAGE_SIZE,
  })

  const setSearch = (value: string) => {
    // Replace rather than push: typing shouldn't bury the previous page under a history entry per
    // keystroke. Any search change resets to page 1, since page 3 of the old results is meaningless
    // against a new filter.
    void navigate({ search: { q: value || undefined, page: undefined }, replace: true })
  }

  const totalPages = data ? Math.max(1, Math.ceil(data.totalCount / data.pageSize)) : 1

  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Clients</h1>
          <p className="text-muted-foreground">
            {data ? `${data.totalCount} total` : 'Organisations using RobTime.'}
          </p>
        </div>
        {/* Styled as a button but rendered as a Link: it navigates, so it must stay a real anchor
            for middle-click, open-in-new-tab, and screen-reader semantics. */}
        <Link to="/setup/clients/new" className={buttonVariants()}>
          New client
        </Link>
      </div>

      <Input
        placeholder="Search by name…"
        defaultValue={q ?? ''}
        onChange={(event) => setSearch(event.target.value)}
        className="max-w-sm"
      />

      {isError && (
        <p className="text-sm text-destructive">
          {toApiProblem(error, 'Could not load clients.').message}
        </p>
      )}

      {isPending ? (
        <p className="text-sm text-muted-foreground">Loading…</p>
      ) : data && data.items.length > 0 ? (
        <div className="overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="border-b bg-muted/50 text-left">
              <tr>
                <th className="px-4 py-2 font-medium">Name</th>
                <th className="px-4 py-2 font-medium">Created by</th>
                <th className="px-4 py-2 font-medium">Created</th>
              </tr>
            </thead>
            <tbody>
              {data.items.map((client) => (
                <tr key={client.id} className="border-b last:border-0 hover:bg-muted/40">
                  <td className="px-4 py-2">
                    <Link
                      to="/setup/clients/$clientId"
                      params={{ clientId: String(client.id) }}
                      className="font-medium underline-offset-4 hover:underline"
                    >
                      {client.name}
                    </Link>
                  </td>
                  <td className="px-4 py-2 text-muted-foreground">{client.createdBy}</td>
                  <td className="px-4 py-2 text-muted-foreground">
                    {new Date(client.createdDate).toLocaleDateString()}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p className="text-sm text-muted-foreground">
          {q ? `No clients match “${q}”.` : 'No clients yet. Create the first one to get started.'}
        </p>
      )}

      {totalPages > 1 && (
        <div className="flex items-center gap-3">
          <Button
            variant="outline"
            size="sm"
            disabled={currentPage <= 1}
            onClick={() => void navigate({ search: { q, page: currentPage - 1 } })}
          >
            Previous
          </Button>
          <span className="text-sm text-muted-foreground">
            Page {currentPage} of {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            disabled={currentPage >= totalPages}
            onClick={() => void navigate({ search: { q, page: currentPage + 1 } })}
          >
            Next
          </Button>
        </div>
      )}
    </div>
  )
}
