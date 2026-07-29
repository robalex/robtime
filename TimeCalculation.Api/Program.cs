using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Amazon;
using Amazon.CognitoIdentityProvider;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using TimeCalculation.Api;
using TimeCalculation.Api.Auth;
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
// Read INSIDE the AddDbContext callback, not captured into a local variable beforehand — this isn't
// stylistic. WebApplicationFactory<Program> (TimeCalculation.Api.Tests) layers a Testcontainers
// connection string onto builder.Configuration via ConfigureAppConfiguration, but that layering
// happens after this line of Program.cs already runs and before the callback below actually
// executes (which happens when the DI container first resolves PayrollDbContext, not when
// AddDbContext is called). A variable captured here would freeze in whatever appsettings.Development
// .json says — the real local-dev Postgres — silently defeating the test suite's "isolated ephemeral
// Postgres" guarantee in favor of local dev's, which happened undetected until TenantIsolationTests'
// ad-hoc DbContext (built directly from the Testcontainers connection string, bypassing DI/this
// callback entirely) exposed the mismatch: it saw an empty, unmigrated database while the DI-resolved
// context serving every other test was quietly reading/writing local dev the whole time. Reading
// builder.Configuration fresh inside the callback picks up whatever's current when the container
// actually builds the context, test override included.
builder.Services.AddDbContext<PayrollDbContext>(options =>
{
    // Staging/production supply the password via App Runner's RuntimeEnvironmentSecrets, pulled
    // directly from the RDS-managed Secrets Manager secret at deploy time — Terraform never reads or
    // stores it (infra/modules/database's manage_master_user_password design). That secret is JSON
    // (host/port/dbname/username/password), not a preformed connection string, so App Runner injects
    // its pieces as separate values (Database:Host/Port/Name/Username plain, Database:Password from
    // the secret) and this composes them. ConnectionStrings:PayrollDb still wins when set directly
    // (local dev, tests, appsettings) — this fallback only fires when that's absent.
    var connectionString = builder.Configuration.GetConnectionString("PayrollDb");
    if (string.IsNullOrEmpty(connectionString))
    {
        var dbHost = builder.Configuration["Database:Host"];
        var dbPassword = builder.Configuration["Database:Password"];
        if (!string.IsNullOrEmpty(dbHost) && !string.IsNullOrEmpty(dbPassword))
        {
            connectionString = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = dbHost,
                Port = int.Parse(builder.Configuration["Database:Port"] ?? "5432"),
                Database = builder.Configuration["Database:Name"],
                Username = builder.Configuration["Database:Username"],
                Password = dbPassword,
                SslMode = Npgsql.SslMode.Require,
            }.ConnectionString;
        }
    }

    if (string.IsNullOrEmpty(connectionString))
    {
        throw new InvalidOperationException(
            $"No 'PayrollDb' connection string found for environment '{builder.Environment.EnvironmentName}'. " +
            "Set it in the matching appsettings file, in user-secrets, via the " +
            "ConnectionStrings__PayrollDb environment variable, or via the discrete Database__Host/" +
            "Port/Name/Username/Password environment variables.");
    }

    // Never add .EnableSensitiveDataLogging() here. EF Core already masks parameter values in its
    // own query logs by default ("Parameters=[@p0='?', ...]") — that's the one piece of PII-in-logs
    // protection this codebase gets for free, and turning that flag on for local debugging is
    // exactly the kind of thing that accidentally survives into a commit. There's also no HTTP
    // request/response body logging wired up (no UseHttpLogging()) — if that's ever added,
    // employee/pay endpoint bodies need to be excluded, not just trusted to redact themselves.
    options.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime());
});

// Resolves PayrollDbContext's ITenantContextAccessor constructor parameter via DI — AddDbContext's
// default factory (ActivatorUtilities) fills in any constructor parameter it can find a registered
// service for, so this needs no extra wiring beyond being registered Scoped, matching the context's
// own per-request lifetime (UI_PLAN.md §5).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContextAccessor, HttpContextTenantContextAccessor>();

// Amazon Cognito issues the JWT; this only validates it.
//
// The Authority is the OIDC *issuer* — https://cognito-idp.{region}.amazonaws.com/{userPoolId} —
// which AddJwtBearer uses to discover the JWKS signing keys and to check each token's `iss` claim.
// It's derived here rather than configured separately, precisely because it's a pure function of two
// values already required: a standalone setting could drift out of sync with the pool id and produce
// an issuer-validation failure that reads like a signing problem. Note this is NOT the managed login
// domain the browser signs in against (that's a different host and doesn't affect the issuer, even
// when customized).
//
// Real values come from user-secrets locally (see RobTimeUI/README.md); appsettings.Development.json
// is committed and holds placeholders. Placeholders don't break startup — the JWKS document is only
// fetched lazily, on first token validation — so this stays safe to wire up without repeating the
// OpenAPI build-time-generation mistake of an eager external check breaking `dotnet build`/`run`.
var cognitoRegion = builder.Configuration["Cognito:Region"];
var cognitoUserPoolId = builder.Configuration["Cognito:UserPoolId"];

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = $"https://cognito-idp.{cognitoRegion}.amazonaws.com/{cognitoUserPoolId}";
        options.Audience = builder.Configuration["Cognito:UserPoolClientId"];
        // Keep claim names exactly as Cognito issues them ("sub", "custom:client_id", "custom:role")
        // instead of ASP.NET Core's default inbound remapping to long-form XML schema URIs.
        options.MapInboundClaims = false;
    });
builder.Services.AddAuthorizationBuilder().AddRolePolicies();

// AWS SDK clients are thread-safe and meant to be long-lived, so Singleton. The region is read here
// (not left to the SDK's own fallback chain) specifically so construction never throws for a missing
// region at startup — the exact class of eager-external-check failure the OpenAPI build-time doc
// generation mistake already taught this project to avoid. Credential resolution stays lazy (the AWS
// default credential chain, resolved per call), so this is still safe to construct with no real AWS
// credentials in this environment; it just means no call through it will succeed yet.
builder.Services.AddSingleton<IAmazonCognitoIdentityProvider>(sp =>
{
    var region = sp.GetRequiredService<IConfiguration>()["Cognito:Region"] ?? "us-east-1";
    return new AmazonCognitoIdentityProviderClient(RegionEndpoint.GetBySystemName(region));
});
builder.Services.AddScoped<ICognitoUserProvisioner, CognitoUserProvisioner>();
builder.Services.AddScoped<UserProvisioningService>();

// Endpoints depend on these, never on PayrollDbContext directly (see CLAUDE.md's Code Style rules —
// no business logic or DB access in endpoints). Scoped to match PayrollDbContext's own lifetime.
builder.Services.AddScoped<ClientService>();
builder.Services.AddScoped<EmployeeService>();
builder.Services.AddScoped<PositionService>();
builder.Services.AddScoped<PayRuleService>();
builder.Services.AddScoped<PunchService>();
builder.Services.AddScoped<PositionAssignmentService>();
builder.Services.AddScoped<PayRuleAssignmentService>();
builder.Services.AddScoped<DifferentialRuleService>();
builder.Services.AddScoped<PayRuleWhatIfService>();
builder.Services.AddScoped<HolidayCalendarService>();
builder.Services.AddScoped<StateMinimumWageService>();
builder.Services.AddScoped<ClientPremiumPolicyService>();
builder.Services.AddScoped<CurrentUserService>();
// No DB dependency (reads a static in-engine registry), so Singleton — no PayrollDbContext lifetime
// to match, and there's nothing about it that needs a fresh instance per request.
builder.Services.AddSingleton<PremiumMetadataService>();
builder.Services.AddSingleton<PayRuleTemplateMetadataService>();
builder.Services.AddSingleton<HolidayMetadataService>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi(options =>
{
    // .NET's schema generator documents every number as `"type": ["integer"|"number", "string"]`
    // with a numeric pattern — permissive enough to cover a JSON number OR a numeric string. This
    // API does neither: System.Text.Json here uses default number handling (no
    // AllowReadingFromString), so it only accepts real JSON numbers and only ever emits them. Left
    // alone, the union propagates into the generated TypeScript as `number | string` on EVERY
    // numeric field — including every wage, rate, and hours quantity in the payroll model — forcing
    // a coercion at each read site in the UI forever. Narrowing it here makes the document match
    // actual behaviour and keeps the generated client honest.
    //
    // Deliberately scoped to the numeric+string union: a schema that is genuinely just "string"
    // (or a real union we meant) is untouched. Query-string parameters keep their own union, which
    // is correct — those really do arrive as text.
    options.AddSchemaTransformer((schema, _, _) =>
    {
        if (schema.Type is { } type && type.HasFlag(JsonSchemaType.String))
        {
            if (type.HasFlag(JsonSchemaType.Integer))
            {
                schema.Type = type.HasFlag(JsonSchemaType.Null)
                    ? JsonSchemaType.Integer | JsonSchemaType.Null
                    : JsonSchemaType.Integer;
                schema.Pattern = null;
            }
            else if (type.HasFlag(JsonSchemaType.Number))
            {
                schema.Type = type.HasFlag(JsonSchemaType.Null)
                    ? JsonSchemaType.Number | JsonSchemaType.Null
                    : JsonSchemaType.Number;
                schema.Pattern = null;
            }
        }

        return Task.CompletedTask;
    });

    // NodaTime types (`ConfigureForNodaTime` above) serialize as plain ISO strings at runtime, but
    // the schema generator has no built-in knowledge of them and emits an empty `{}` schema —
    // openapi-typescript then has nothing to go on and generates `unknown` for every LocalDate/
    // Instant field, defeating the typed-client whole point (UI_PLAN.md §4). Describing them
    // explicitly here keeps the generated document truthful to the wire format.
    options.AddSchemaTransformer((schema, context, _) =>
    {
        // A HashSet<LocalDate>/HashSet<LocalTime> property (e.g. DifferentialRule.SpecificDates,
        // HolidayCalendar.Dates) reaches this transformer with JsonTypeInfo.Type as the collection
        // itself, not the element — describe schema.Items instead of schema in that case, so the
        // generated array element type is still truthful rather than falling through to `unknown[]`.
        // The base generator leaves Items null for these (LocalDate's custom JsonConverter makes it
        // opaque to reflection-based item-schema generation), so it's created here rather than just
        // patched.
        var targetSchema = schema;
        var type = Nullable.GetUnderlyingType(context.JsonTypeInfo.Type) ?? context.JsonTypeInfo.Type;
        if (context.JsonTypeInfo.Kind == JsonTypeInfoKind.Enumerable
            && context.JsonTypeInfo.ElementType is { } elementType)
        {
            var items = schema.Items as OpenApiSchema ?? new OpenApiSchema();
            schema.Items = items;
            targetSchema = items;
            type = Nullable.GetUnderlyingType(elementType) ?? elementType;
        }

        if (type == typeof(LocalDate))
        {
            targetSchema.Type = JsonSchemaType.String;
            targetSchema.Format = "date";
        }
        else if (type == typeof(LocalTime))
        {
            targetSchema.Type = JsonSchemaType.String;
            targetSchema.Format = "time";
        }
        else if (type == typeof(Instant))
        {
            targetSchema.Type = JsonSchemaType.String;
            targetSchema.Format = "date-time";
        }

        return Task.CompletedTask;
    });
});

// Vite's default dev server port. RobTimeUI doesn't exist yet, so there's no real deployed origin
// to allow — production serves the SPA same-origin behind CloudFront, which needs no CORS policy at
// all. This is dev-only scaffolding for local `npm run dev` against a local API, not a policy to
// widen later; delete it once same-origin proxying is set up, don't just add more origins to it.
// AllowCredentials isn't load-bearing for the Cognito bearer-token flow (UI_PLAN.md §5) the way it
// was for the original cookie-auth design, but costs nothing to leave in place for now.
const string ViteDevCorsPolicy = "ViteDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(ViteDevCorsPolicy, policy => policy
        .WithOrigins("http://localhost:5173")
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
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

// `dotnet run -- --bootstrap-admin you@example.com` links a Cognito user (already created in the
// console — no self-registration, see UI_PLAN.md §5) to a SystemAdmin AppUser row, breaking the
// chicken-and-egg problem where POST /users itself requires an already-authorized caller. See
// AdminBootstrapper's own doc comment.
var bootstrapAdminFlagIndex = Array.IndexOf(args, "--bootstrap-admin");
if (bootstrapAdminFlagIndex >= 0)
{
    var bootstrapEmail = args.ElementAtOrDefault(bootstrapAdminFlagIndex + 1);
    if (string.IsNullOrWhiteSpace(bootstrapEmail))
    {
        Console.WriteLine("Usage: dotnet run -- --bootstrap-admin <email>");
        return;
    }

    var bootstrapUserPoolId = builder.Configuration["Cognito:UserPoolId"]
        ?? throw new InvalidOperationException("Cognito:UserPoolId is not configured.");

    using var bootstrapScope = app.Services.CreateScope();
    var bootstrapCognito = bootstrapScope.ServiceProvider.GetRequiredService<IAmazonCognitoIdentityProvider>();
    var bootstrapDb = bootstrapScope.ServiceProvider.GetRequiredService<PayrollDbContext>();
    await AdminBootstrapper.RunAsync(bootstrapCognito, bootstrapDb, bootstrapUserPoolId, bootstrapEmail, CancellationToken.None);
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

// Unconditional (not just Development) — Production needs authenticated requests validated too.
app.UseAuthentication();
app.UseAuthorization();

app.MapClientEndpoints();
app.MapEmployeeEndpoints();
app.MapPositionEndpoints();
app.MapPositionAssignmentEndpoints();
app.MapPayRuleAssignmentEndpoints();
app.MapPayRuleEndpoints();
app.MapDifferentialRuleEndpoints();
app.MapHolidayCalendarEndpoints();
app.MapStateMinimumWageEndpoints();
app.MapClientPremiumPolicyEndpoints();
app.MapPunchEndpoints();
app.MapMetadataEndpoints();
app.MapUserEndpoints();
app.MapMeEndpoints();

app.Run();

public partial class Program;
