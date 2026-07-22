using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Api.Endpoints;
using TimeCalculation.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

    // Accept and emit enums as their names ("In", "IntervalWithGrace") rather than opaque ordinals,
    // so payloads are self-describing and stay valid if an enum's members are ever reordered.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<IClock>(SystemClock.Instance);

// Resolved per environment from appsettings.{EnvironmentName}.json, overridable by the
// ConnectionStrings__PayrollDb environment variable — which is how staging/production should supply
// it, never a committed file. `dotnet ef` boots this same host, so migrations target whichever
// environment is selected (see TimeCalculation.Persistence/README.md).
var connectionString = builder.Configuration.GetConnectionString("PayrollDb")
    ?? throw new InvalidOperationException(
        $"No 'PayrollDb' connection string found for environment '{builder.Environment.EnvironmentName}'. " +
        "Set it in the matching appsettings file, in user-secrets, or via the " +
        "ConnectionStrings__PayrollDb environment variable.");

builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapClientEndpoints();
app.MapEmployeeEndpoints();
app.MapPayRuleEndpoints();
app.MapPunchEndpoints();

app.Run();

public partial class Program;
