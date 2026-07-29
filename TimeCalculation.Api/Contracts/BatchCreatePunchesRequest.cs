namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Phase 6.8's fast bulk-entry grid: a week (or however many rows a supervisor typed) submitted as
/// one request rather than one <c>POST /punches</c> per row. Each entry is a full
/// <see cref="CreatePunchRequest"/> — same shape, same per-row validation — not a lighter row type,
/// so the single-punch and batch paths can never quietly drift apart on what a punch requires.
/// </summary>
public sealed record BatchCreatePunchesRequest
{
    public required List<CreatePunchRequest> Punches { get; init; }
}
