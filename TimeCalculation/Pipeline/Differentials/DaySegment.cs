using NodaTime;

namespace TimeCalculation.Pipeline.Differentials;

// A worked interval's slice within a single local date, expressed as [StartSec, EndSec)
// seconds-of-day. A segment ending at the next local midnight reports EndSec as SecondsPerDay.
// DayStart is that date's local midnight as a real Instant — carried along so a seconds-of-day
// overlap range can be converted back into actual Instants (DayStart + Duration.FromSeconds(sec))
// without a second zone conversion.
internal readonly record struct DaySegment(LocalDate Date, Instant DayStart, int StartSec, int EndSec);
