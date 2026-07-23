namespace TimeCalculation.Api.Services;

public enum ServiceResultKind
{
    Success,
    ValidationFailed,
    NotFound,
    Conflict,
}

/// <summary>
/// What a service call produced: exactly one success value, or exactly one of a small set of named
/// failure shapes — never a bare string, and never a tuple standing in for "the thing that went
/// wrong plus why" (see CLAUDE.md's Code Style rules). Endpoints pattern-match <see cref="Kind"/>
/// to pick the matching <c>TypedResults.*</c> response; that's the one place multiple returns are
/// expected, same carve-out CLAUDE.md already documents for minimal-API handlers — a service
/// method building one of these should still return it exactly once.
/// </summary>
public sealed record ServiceResult<T>
{
    public required ServiceResultKind Kind { get; init; }
    public T? Value { get; init; }
    public IDictionary<string, string[]>? ValidationErrors { get; init; }

    /// <summary>Human-readable detail for NotFound/Conflict — becomes ProblemDetails.Detail.</summary>
    public string? Detail { get; init; }

    public static ServiceResult<T> Success(T value) =>
        new() { Kind = ServiceResultKind.Success, Value = value };

    public static ServiceResult<T> ValidationFailed(IDictionary<string, string[]> errors) =>
        new() { Kind = ServiceResultKind.ValidationFailed, ValidationErrors = errors };

    public static ServiceResult<T> NotFound(string detail) =>
        new() { Kind = ServiceResultKind.NotFound, Detail = detail };

    public static ServiceResult<T> Conflict(string detail) =>
        new() { Kind = ServiceResultKind.Conflict, Detail = detail };
}
