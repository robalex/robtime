using TimeCalculation.Model;

namespace TimeCalculation.Ingestion;

/// <summary>
/// Pure dedup for punch ingest.  A punch that carries a (EmployeeId, DeviceId, DevicePunchId) key
/// already present — either among previously stored punches or earlier in the same batch — is a
/// device retry and is dropped.  Punches without a device key are never deduped (nothing to match on).
/// This mirrors the unique constraint the persistence layer enforces.
/// </summary>
public static class IdempotentIngest
{
    public static IReadOnlyList<Punch> Deduplicate(
        IEnumerable<Punch> existing, IEnumerable<Punch> incoming)
    {
        var seen = new HashSet<(int, string, string)>();
        foreach (var p in existing)
            if (TryKey(p, out var key)) seen.Add(key);

        var accepted = new List<Punch>();
        foreach (var p in incoming)
        {
            if (TryKey(p, out var key))
            {
                if (!seen.Add(key)) continue;   // duplicate of stored or earlier-in-batch
            }
            accepted.Add(p);
        }
        return accepted;
    }

    private static bool TryKey(Punch p, out (int, string, string) key)
    {
        if (!string.IsNullOrEmpty(p.DeviceId) && !string.IsNullOrEmpty(p.DevicePunchId))
        {
            key = (p.EmployeeId, p.DeviceId, p.DevicePunchId);
            return true;
        }
        key = default;
        return false;
    }
}
