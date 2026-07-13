using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapPost("/clients", CreateClient).WithName("CreateClient");
    }

    private static async Task<Results<Created<Client>, ValidationProblem>> CreateClient(
        CreateClientRequest request, PayrollDbContext db, IClock clock, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["name"] = ["Name is required."],
            });
        }

        var client = new Client
        {
            Name = request.Name,
            CreatedBy = request.CreatedBy,
            CreatedDate = clock.GetCurrentInstant().ToDateTimeUtc(),
        };

        db.Clients.Add(client);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/clients/{client.Id}", client);
    }
}
