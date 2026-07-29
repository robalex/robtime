import type { components } from '@/api/schema'

export type Me = components['schemas']['MeResponse']
export type AppRole = components['schemas']['AppRole']

/**
 * The single place role checks live (UI_PLAN.md §5). Everything keys off the `/me` response — never
 * off token presence, and never off a role string sprinkled through components. When the anticipated
 * restricted-supervisor tier arrives (§11), it's a branch in this file rather than a hunt through
 * the component tree.
 *
 * This is a UX affordance layer, not a security boundary: the API enforces the same rules server-side
 * via authorization policies and tenant query filters. Hiding a button here never substitutes for
 * that — it just avoids showing someone an action that would 403.
 */
const RANK: Record<AppRole, number> = {
  Employee: 0,
  Supervisor: 1,
  ClientAdmin: 2,
  SystemAdmin: 3,
}

function atLeast(me: Me | undefined, role: AppRole): boolean {
  if (!me?.role) {
    return false
  }
  return RANK[me.role] >= RANK[role]
}

export const can = {
  /** Creating and listing clients is inherently cross-tenant — SystemAdmin only. */
  manageClients: (me: Me | undefined) => atLeast(me, 'SystemAdmin'),
  manageEmployees: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  manageUsers: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  managePayRules: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  manageDifferentialRules: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  manageHolidayCalendars: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  manageClientPremiumPolicies: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  /** A bulk import can create or hard-delete punches for any employee in the tenant at once — closer
   * in kind to Position/PayRule administration than to a supervisor fixing one employee's shift, so
   * it sits at the same bar as those rather than Supervisor's per-punch edit access. */
  importPunches: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
  /** Shared reference data across every client — not a per-tenant setting. */
  manageStateMinimumWages: (me: Me | undefined) => atLeast(me, 'SystemAdmin'),
  /** Supervisor sees wage rates and pay amounts (§9 decision 16). */
  viewPay: (me: Me | undefined) => atLeast(me, 'Supervisor'),
  approvePunchChanges: (me: Me | undefined) => atLeast(me, 'Supervisor'),
  /** Approving/un-approving a timecard locks/unlocks its period (Phase 6.7, UI_PLAN.md decision 21)
   * — a distinct action from deciding a single PunchChangeRequest, kept as its own permission even
   * though both currently resolve to the same Supervisor+ bar. */
  approveTimecard: (me: Me | undefined) => atLeast(me, 'Supervisor'),

  /**
   * People is where per-employee records are managed *and* where a supervisor reads someone else's
   * timecard (UI_PLAN.md Phase 6's first/third-person split), so it's Supervisor+ rather than
   * ClientAdmin — a supervisor who can't open the destination can't review the timecards they're
   * meant to sign off. The mutating screens inside it stay gated by `manageEmployees`.
   */
  viewPeople: (me: Me | undefined) => atLeast(me, 'Supervisor'),

  /** Setup is entirely client/system configuration — nothing in it is an employee's business. */
  viewSetup: (me: Me | undefined) => atLeast(me, 'ClientAdmin'),
}
