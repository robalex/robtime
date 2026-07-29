namespace TimeCalculation.Api.Contracts;

public sealed record DecidePunchChangeRequestRequest
{
    public required bool Approve { get; init; }
    public string? ReviewNote { get; init; }
}
