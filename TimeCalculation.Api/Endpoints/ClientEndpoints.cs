using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;
using TimeCalculation.Model;

namespace TimeCalculation.Api.Endpoints;

public static class ClientEndpoints
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        app.MapPost("/clients", CreateClient).WithName("CreateClient");
    }

    private static async Task<Results<Created<Client>, ValidationProblem>> CreateClient(
        CreateClientRequest request, ClientService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created($"/clients/{result.Value!.Id}", result.Value),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for client creation."),
        };
    }
}
