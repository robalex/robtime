using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Api;
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
// `dotnet ef` boots this same composition root for migrations, so this also needs to resolve under
// whatever environment migrations are run against. The OpenAPI doc-generation target
// (GenerateOpenApiDocuments, see the .csproj) also boots this via HostFactoryResolver, but is no
// longer wired into a plain `dotnet build`/`dotnet test` — it's opt-in only, specifically so this
// check doesn't need ASPNETCORE_ENVIRONMENT set just to compile. If invoking that target manually,
// set ASPNETCORE_ENVIRONMENT=Development first (Production has no committed connection string, by
// design — see below).
var connectionString = builder.Configuration.GetConnectionString("PayrollDb")
    ?? throw new InvalidOperationException(
        $"No 'PayrollDb' connection string found for environment '{builder.Environment.EnvironmentName}'. " +
        "Set it in the matching appsettings file, in user-secrets, or via the " +
        "ConnectionStrings__PayrollDb environment variable.");

// Never add .EnableSensitiveDataLogging() here. EF Core already masks parameter values in its own
// query logs by default ("Parameters=[@p0='?', ...]") — that's the one piece of PII-in-logs
// protection this codebase gets for free, and turning that flag on for local debugging is exactly
// the kind of thing that accidentally survives into a commit. There's also no HTTP request/response
// body logging wired up (no UseHttpLogging()) — if that's ever added, employee/pay endpoint bodies
// need to be excluded, not just trusted to redact themselves.
builder.Services.AddDbContext<PayrollDbContext>(options =>
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()));

// Endpoints depend on these, never on PayrollDbContext directly (see CLAUDE.md's Code Style rules —
// no business logic or DB access in endpoints). Scoped to match PayrollDbContext's own lifetime.
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<PositionService>();
builder.Services.AddScoped<PayRuleService>();
builder.Services.AddScoped<PunchService>();
// No DB dependency (reads a static in-engine registry), so Singleton — no PayrollDbContext lifetime
// to match, and there's nothing about it that needs a fresh instance per request.
builder.Services.AddSingleton<PremiumMetadataService>();

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

// `dotnet run -- --seed` populates a freshly-migrated empty database with demo data (DevSeeder),
// then exits without starting the web host — a local-dev convenience, not a real deployment path.
if (args.Contains("--seed"))
{
    using var seedScope = app.Services.CreateScope();
    var seedDb = seedScope.ServiceProvider.GetRequiredService<PayrollDbContext>();
    var seedClock = seedScope.ServiceProvider.GetRequiredService<IClock>();
    await DevSeeder.SeedAsync(seedDb, seedClock, CancellationToken.None);
    return;
}

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
app.MapPositionEndpoints();
app.MapPayRuleEndpoints();
app.MapPunchEndpoints();
app.MapMetadataEndpoints();

app.Run();

public partial class Program;
