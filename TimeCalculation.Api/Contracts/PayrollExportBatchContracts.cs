using NodaTime;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

public sealed record CreatePayrollExportRequest
{
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }
}

/// <summary>Deliberately omits FileContent — the list/create responses stay light; only the
/// download endpoint touches the byte column, and it returns the raw file, not this DTO.</summary>
public sealed record PayrollExportBatchResponse
{
    public required int Id { get; init; }
    public required int ProfileId { get; init; }
    public required LocalDate PeriodStart { get; init; }
    public required LocalDate PeriodEnd { get; init; }
    public required int EmployeeCount { get; init; }
    public required int RowCount { get; init; }
    public required decimal TotalAmount { get; init; }
    public required string FileName { get; init; }
    public required string ExportedByUserId { get; init; }
    public required Instant ExportedAt { get; init; }
    public string? VoidedByUserId { get; init; }
    public Instant? VoidedAt { get; init; }

    public static PayrollExportBatchResponse FromEntity(PayrollExportBatch batch) => new()
    {
        Id = batch.Id,
        ProfileId = batch.ProfileId,
        PeriodStart = batch.PeriodStart,
        PeriodEnd = batch.PeriodEnd,
        EmployeeCount = batch.EmployeeCount,
        RowCount = batch.RowCount,
        TotalAmount = batch.TotalAmount,
        FileName = batch.FileName,
        ExportedByUserId = batch.ExportedByUserId,
        ExportedAt = batch.ExportedAt,
        VoidedByUserId = batch.VoidedByUserId,
        VoidedAt = batch.VoidedAt,
    };
}
