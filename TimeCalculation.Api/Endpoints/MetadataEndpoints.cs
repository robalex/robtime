using Microsoft.AspNetCore.Http.HttpResults;
using NodaTime;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class MetadataEndpoints
{
    public static void MapMetadataEndpoints(this WebApplication app)
    {
        app.MapGet("/metadata/premium-rules", GetPremiumRules).WithName("GetPremiumRuleMetadata").RequireAuthorization();
        app.MapGet("/metadata/pay-rule-templates", GetPayRuleTemplates).WithName("GetPayRuleTemplates").RequireAuthorization();
        app.MapGet("/metadata/us-federal-holidays", GetUsFederalHolidays).WithName("GetUsFederalHolidays").RequireAuthorization();
    }

    // Typed return (not bare IResult) so the OpenAPI generator can infer a response schema — a bare
    // IResult erases the payload type and the generated TS client sees no content at all.
    private static Ok<List<PremiumRuleMetadataResponse>> GetPremiumRules(PremiumMetadataService service)
    {
        var rules = service.GetAll().Select(PremiumRuleMetadataResponse.FromRule).ToList();
        return TypedResults.Ok(rules);
    }

    private static Ok<List<PayRuleTemplateResponse>> GetPayRuleTemplates(PayRuleTemplateMetadataService service)
    {
        var templates = service.GetAll().Select(PayRuleTemplateResponse.FromTemplate).ToList();
        return TypedResults.Ok(templates);
    }

    // A convenience preset for the holiday calendar editor — "add federal holidays for <year>" — not
    // a source of truth the engine reads from; a client's HolidayCalendar.Dates is its own copied,
    // editable set once seeded.
    private static Ok<List<LocalDate>> GetUsFederalHolidays(int year, HolidayMetadataService service)
    {
        return TypedResults.Ok(service.GetUsFederalHolidays(year).ToList());
    }
}
