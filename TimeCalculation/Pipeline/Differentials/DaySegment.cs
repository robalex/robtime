using NodaTime;

namespace TimeCalculation.Pipeline.Differentials;

// A worked interval's slice within a single local date, expressed as [StartSec, EndSec)
// seconds-of-day. A segment ending at the next local midnight reports EndSec as SecondsPerDay.
internal readonly record struct DaySegment(LocalDate Date, int StartSec, int EndSec);
