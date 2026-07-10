namespace TimeCalculation.Ingestion;

public interface IPunchAuditWriter
{
    Task WriteAsync(PunchAuditEntry entry, CancellationToken ct = default);
}
