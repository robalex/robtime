using NodaTime;

namespace TimeCalculation.Api.Contracts;

public record ResolveLocalPunchTimeResponse
{
    public required Instant PunchTime { get; init; }
}
