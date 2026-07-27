import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api } from '@/api/client'
import type { components } from '@/api/schema'

export type HolidayCalendar = components['schemas']['HolidayCalendarResponse']
export type CreateHolidayCalendar = components['schemas']['CreateHolidayCalendarRequest']
export type UpdateHolidayCalendar = components['schemas']['UpdateHolidayCalendarRequest']

export interface HolidayCalendarListParams {
  clientId: number
  page: number
  pageSize: number
}

export const holidayCalendarKeys = {
  all: ['holidayCalendars'] as const,
  lists: () => [...holidayCalendarKeys.all, 'list'] as const,
  list: (params: HolidayCalendarListParams) => [...holidayCalendarKeys.lists(), params] as const,
  details: () => [...holidayCalendarKeys.all, 'detail'] as const,
  detail: (id: number) => [...holidayCalendarKeys.details(), id] as const,
}

export function useHolidayCalendars(params: HolidayCalendarListParams | null) {
  return useQuery({
    queryKey: params ? holidayCalendarKeys.list(params) : [...holidayCalendarKeys.lists(), 'none'],
    enabled: params !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/holidaycalendars', {
        params: { query: { clientId: params!.clientId, Page: params!.page, PageSize: params!.pageSize } },
      })
      if (error) {
        throw error
      }
      return data
    },
    placeholderData: (previous) => previous,
  })
}

export function useHolidayCalendar(id: number) {
  return useQuery({
    queryKey: holidayCalendarKeys.detail(id),
    queryFn: async () => {
      const { data, error } = await api.GET('/holidaycalendars/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}

export function useCreateHolidayCalendar() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: CreateHolidayCalendar) => {
      const { data, error } = await api.POST('/holidaycalendars', { body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: holidayCalendarKeys.lists() }),
  })
}

export function useUpdateHolidayCalendar(id: number) {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (body: UpdateHolidayCalendar) => {
      const { data, error } = await api.PUT('/holidaycalendars/{id}', { params: { path: { id } }, body })
      if (error) {
        throw error
      }
      return data
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: holidayCalendarKeys.detail(id) })
      void queryClient.invalidateQueries({ queryKey: holidayCalendarKeys.lists() })
    },
  })
}

export function useDeleteHolidayCalendar() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: async (id: number) => {
      const { error } = await api.DELETE('/holidaycalendars/{id}', { params: { path: { id } } })
      if (error) {
        throw error
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: holidayCalendarKeys.all }),
  })
}

// Static per-year data (like premium/pay-rule-template metadata) — a convenience preset for the
// date-list editor, not something the engine reads from.
export function useUsFederalHolidays(year: number | null) {
  return useQuery({
    queryKey: ['usFederalHolidays', year],
    enabled: year !== null,
    queryFn: async () => {
      const { data, error } = await api.GET('/metadata/us-federal-holidays', { params: { query: { year: year! } } })
      if (error) {
        throw error
      }
      return data
    },
  })
}
