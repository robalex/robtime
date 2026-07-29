using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Services;

/// <summary>
/// Serializes/deserializes <see cref="PayCalculationSnapshot"/> for <c>TimecardApproval.SnapshotJson</c>
/// — same convention as <see cref="PunchAuditor"/>'s punch snapshots: a dedicated NodaTime-aware
/// <see cref="JsonSerializerOptions"/> instance, since this is internal storage, not an HTTP response
/// (Program.cs configures a separate instance for that).
/// </summary>
public static class TimecardSnapshotSerializer
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static string Serialize(PayCalculationSnapshot snapshot) =>
        JsonSerializer.Serialize(snapshot, Options);

    public static PayCalculationSnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<PayCalculationSnapshot>(json, Options)
        ?? throw new InvalidOperationException("Stored timecard snapshot deserialized to null.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
