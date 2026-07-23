using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace TimeCalculation.Api.Tests;

/// <summary>Mirrors Program.cs's ConfigureHttpJsonOptions exactly, so request/response bodies in
/// tests (PayRule's LocalDate fields, enums serialized as names) round-trip the same way a real
/// client would see them.</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
