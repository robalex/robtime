using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class MetadataEndpoints
{
    public static void MapMetadataEndpoints(this WebApplication app)
    {
        app.MapGet("/metadata/premium-rules", GetPremiumRules).WithName("GetPremiumRuleMetadata");
    }

    private static IResult GetPremiumRules(PremiumMetadataService service)
    {
        var rules = service.GetAll().Select(PremiumRuleMetadataResponse.FromRule).ToList();
        return TypedResults.Ok(rules);
    }
}
