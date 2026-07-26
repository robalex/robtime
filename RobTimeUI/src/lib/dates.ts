import { LocalDate } from '@js-joda/core'

/**
 * The NodaTime/js-joda boundary (UI_PLAN.md §4). The API sends/accepts `LocalDate` as a bare ISO
 * string ("2026-07-22") — the generated `components["schemas"]["LocalDate"]` type is exactly
 * `string`, indistinguishable from any other string. Branding it here means a raw wire string can't
 * be passed where a parsed date is expected, and vice versa, without going through parse/toWire.
 */
export type LocalDateString = string & { readonly __brand: 'LocalDate' }

export function parseLocalDate(value: string): LocalDate {
  return LocalDate.parse(value)
}

export function toWireLocalDate(date: LocalDate): LocalDateString {
  return date.toString() as LocalDateString
}

/** Display format — "Jul 22, 2026". Always through here so every screen reads dates the same way. */
export function formatLocalDate(date: LocalDate): string {
  return new Date(date.year(), date.monthValue() - 1, date.dayOfMonth()).toLocaleDateString('en-US', {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
  })
}

export function todayLocalDate(): LocalDate {
  return LocalDate.now()
}
