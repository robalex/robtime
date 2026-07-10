using NodaTime;
using TimeCalculation.Model;

namespace TimeCalculation.Ingestion;

public interface IPunchRepository
{
    Task<Punch?> GetByIdAsync(int id, CancellationToken ct = default);

    Task<IReadOnlyList<Punch>> GetForEmployeeAsync(
        int employeeId, Instant from, Instant to, CancellationToken ct = default);

    /// <summary>Returns the employee's most recent non-deleted punch regardless of time range.</summary>
    Task<Punch?> GetMostRecentForEmployeeAsync(int employeeId, CancellationToken ct = default);

    Task<int> AddAsync(Punch punch, CancellationToken ct = default);
    Task UpdateAsync(Punch punch, CancellationToken ct = default);
    Task SoftDeleteAsync(int id, string deletedBy, CancellationToken ct = default);
}
