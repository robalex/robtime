// Identity colours only — which differential a block belongs to. Kept separate from any semantic
// won/lost styling a later slice adds, so "this is NIGHT" and "NIGHT won here" never fight over the
// same hue. Indexed off a code's position in a stable, alphabetically-sorted list (the pay rule's
// full ActiveDifferentialCodes set, not just whatever appears in the current window) so a code keeps
// its colour across window/day-count changes for the same pay rule.
const PALETTE = [
  'bg-blue-500/70 border-blue-600 dark:bg-blue-500/50 dark:border-blue-400',
  'bg-violet-500/70 border-violet-600 dark:bg-violet-500/50 dark:border-violet-400',
  'bg-emerald-500/70 border-emerald-600 dark:bg-emerald-500/50 dark:border-emerald-400',
  'bg-amber-500/70 border-amber-600 dark:bg-amber-500/50 dark:border-amber-400',
  'bg-rose-500/70 border-rose-600 dark:bg-rose-500/50 dark:border-rose-400',
  'bg-cyan-500/70 border-cyan-600 dark:bg-cyan-500/50 dark:border-cyan-400',
  'bg-fuchsia-500/70 border-fuchsia-600 dark:bg-fuchsia-500/50 dark:border-fuchsia-400',
  'bg-lime-500/70 border-lime-600 dark:bg-lime-500/50 dark:border-lime-400',
] as const

export function buildZoneColorMap(codes: readonly string[]): Map<string, string> {
  const sorted = [...codes].sort((a, b) => a.localeCompare(b))
  return new Map(sorted.map((code, index) => [code, PALETTE[index % PALETTE.length]]))
}
