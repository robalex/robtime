using NodaTime;

namespace TimeCalculation.Api.Contracts;

public sealed record PunchImportBatchResponse
{
    public required int Id { get; init; }
    public required string FileName { get; init; }
    public required int PunchCount { get; init; }
    public required string ImportedByUserId { get; init; }
    public required Instant ImportedAt { get; init; }
    public string? DeletedByUserId { get; init; }
    public Instant? DeletedAt { get; init; }

    public static PunchImportBatchResponse FromEntity(Persistence.PunchImportBatch batch) => new()
    {
        Id = batch.Id,
        FileName = batch.FileName,
        PunchCount = batch.PunchCount,
        ImportedByUserId = batch.ImportedByUserId,
        ImportedAt = batch.ImportedAt,
        DeletedByUserId = batch.DeletedByUserId,
        DeletedAt = batch.DeletedAt,
    };
}
