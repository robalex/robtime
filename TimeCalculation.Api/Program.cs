using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Api.Endpoints;
using TimeCalculation.Api.Services;
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

// One error shape everywhere: TypedResults.ValidationProblem already emits RFC 7807
// application/problem+json for validation failures; this extends the same shape to every other
// error response (not-found, conflict, and any unhandled exception) instead of leaving those as
// bare strings.
builder.Services.AddProblemDetails();

// Resolved per environment from appsettings.{EnvironmentName}.json, overridable by the
// ConnectionStrings__PayrollDb environment variable — which is how staging/production should supply
// it, never a committed file. `dotnet ef` boots this same host, so migrations target whichever
// environment is selected (see TimeCalculation.Persistence/README.md).
//
// This also runs during a plain `dotnet build`: the OpenApiGenerateDocumentsOnBuild target
// (see the .csproj) boots this composition root via HostFactoryResolver to introspect routes,
// same as `dotnet ef` does for migrations. A build with no ASPNETCORE_ENVIRONMENT set defaults to
// Production, which has no committed connection string by design (see below) — so a *bare*
// `dotnet build` throws here. Build with `ASPNETCORE_ENVIRONMENT=Development` (CI does this; see
// .github/workflows/ci.yml) to pick up appsettings.Development.json's local-only connection string.
var connectionString = builder.Configuration.GetConnectionString("PayrollDb")
    ?? throw new InvalidOperationException(
        $"No 'PayrollDb' connection string found for environment '{builder.Environment.EnvironmentName}'. " +
        "Set it in the matching appsettings file, in user-secrets, or via the " +
        "ConnectionStrings__PayrollDb environment variable. " +
        "If this is happening during `dotnet build` (not `dotnet run`), set ASPNETCORE_ENVIRONMENT=Development first.");

builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()));

// Endpoints depend on these, never on PayrollDbContext directly (see CLAUDE.md's Code Style rules —
// no business logic or DB access in endpoints). Scoped to match PayrollDbContext's own lifetime.
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<PayRuleService>();
builder.Services.AddScoped<PunchService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

// Vite's default dev server port. RobTimeUI doesn't exist yet, so there's no real deployed origin
// to allow — cookie auth (UI_PLAN.md §5) means production serves the SPA same-origin behind
// CloudFront, which needs no CORS policy at all. This is dev-only scaffolding for local `npm run
// dev` against a local API, not a policy to widen later; delete it once same-origin proxying is
// set up, don't just add more origins to it.
const string ViteDevCorsPolicy = "ViteDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ViteDevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());   // cookie auth needs credentialed CORS requests
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors(ViteDevCorsPolicy);
}
else
{
    // Development gets the built-in developer exception page (auto-registered by
    // WebApplicationBuilder). Everywhere else, an unhandled exception should still come back as
    // application/problem+json via the IProblemDetailsService registered above, not a bare 500.
    app.UseExceptionHandler();
}

app.MapClientEndpoints();
app.MapEmployeeEndpoints();
app.MapPayRuleEndpoints();
app.MapPunchEndpoints();

app.Run();

public partial class Program;
