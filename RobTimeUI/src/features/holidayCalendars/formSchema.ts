import { z } from 'zod'

export const holidayCalendarFormSchema = z.object({
  name: z.string().trim().min(1, 'Name is required.'),
  dates: z.array(z.string()),
})

export type HolidayCalendarFormValues = z.infer<typeof holidayCalendarFormSchema>

export const DEFAULT_HOLIDAY_CALENDAR_FORM_VALUES: HolidayCalendarFormValues = {
  name: '',
  dates: [],
}
