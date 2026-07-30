import { LocalDate } from '@js-joda/core'

/**
 * Converts an Instant (already fully DST-resolved by the engine — see LocalTimeResolver /
 * DifferentialZoneProjector) into the employee's own IANA zone for display, using the browser's
 * built-in Intl API rather than adding a timezone-database dependency (@js-joda/timezone isn't
 * installed, and Intl.DateTimeFormat with a `timeZone` option handles arbitrary IANA zones natively).
 * This is a *positioning* concern only — every decision about whether/when a zone occurs already
 * happened server-side; this just says where to draw it.
 */
export interface ZonedParts {
  date: LocalDate
  secondsOfDay: number
}

export function toZonedParts(instantIso: string, timeZone: string): ZonedParts {
  const parts = new Intl.DateTimeFormat('en-US', {
    timeZone,
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
    hour12: false,
  }).formatToParts(new Date(instantIso))

  const get = (type: string) => Number(parts.find((p) => p.type === type)?.value ?? 0)
  // hour12: false formats midnight as "24" in some locales/engines rather than "00".
  const hour = get('hour') % 24

  return {
    date: LocalDate.of(get('year'), get('month'), get('day')),
    secondsOfDay: hour * 3600 + get('minute') * 60 + get('second'),
  }
}
