import { createFileRoute, Link } from '@tanstack/react-router'
import { Card, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'
import { useMe } from '@/auth/useMe'
import { can } from '@/lib/permissions'

export const Route = createFileRoute('/setup/')({
  component: SetupHome,
})

// A card grid, not a nav menu (UI_PLAN.md §6 Rule 1): each card is self-describing, so you can find
// a config area without already knowing where it lives. Counts and "needs attention" badges belong
// here too once there's data behind them.
const AREAS = [
  {
    to: '/setup/clients',
    title: 'Clients',
    description: 'Organisations using RobTime. Creating one is the first step for a new customer.',
    visible: can.manageClients,
  },
  {
    to: '/setup/payrules',
    title: 'Pay rules',
    description: 'Overtime, rounding, and state premium configuration, built from jurisdiction templates.',
    visible: can.managePayRules,
  },
] as const

function SetupHome() {
  const { data: me } = useMe()
  const areas = AREAS.filter((area) => area.visible(me))

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Setup</h1>
        <p className="text-muted-foreground">Configuration areas available to you.</p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {areas.map((area) => (
          <Link key={area.to} to={area.to} className="group">
            <Card className="h-full transition-colors group-hover:border-ring">
              <CardHeader>
                <CardTitle>{area.title}</CardTitle>
                <CardDescription>{area.description}</CardDescription>
              </CardHeader>
            </Card>
          </Link>
        ))}
      </div>

      {areas.length === 0 && (
        <p className="text-sm text-muted-foreground">
          No configuration areas are available for your role.
        </p>
      )}
    </div>
  )
}
