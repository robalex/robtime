namespace TimeCalculation.Api.Contracts;

/// <summary>
/// Bound via [AsParameters] from query-string values (?page=2&amp;pageSize=50). Page/PageSize are
/// nullable, not defaulted via property initializer — [AsParameters] binds each property against
/// its own declared type, so a non-nullable int is treated as a *required* query parameter and a
/// missing ?page= 400s instead of falling back to the initializer. Verified this live (a bare
/// GET /clients failed with "Required parameter... was not provided" before this was nullable) —
/// worth recording since it's the kind of framework behavior you'd otherwise assume works like a
/// normal C# default.
/// </summary>
public sealed record PagingQuery
{
    public int? Page { get; init; }
    public int? PageSize { get; init; }

    /// <summary>Defaults to 1, never below it — a caller-supplied page=0 or negative page shouldn't
    /// reach EF's Skip().</summary>
    public int NormalizedPage => Math.Max(1, Page ?? 1);

    /// <summary>Defaults to 25, clamped to [1, 100] — caps how much a single request can pull back
    /// regardless of what a caller asks for.</summary>
    public int NormalizedPageSize => Math.Clamp(PageSize ?? 25, 1, 100);
}
