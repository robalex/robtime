using Microsoft.AspNetCore.Http.HttpResults;
using TimeCalculation.Api.Auth;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Api.Services;

namespace TimeCalculation.Api.Endpoints;

public static class HolidayCalendarEndpoints
{
    public static void MapHolidayCalendarEndpoints(this WebApplication app)
    {
        app.MapPost("/holidaycalendars", CreateHolidayCalendar).WithName("CreateHolidayCalendar")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapGet("/holidaycalendars", ListHolidayCalendars).WithName("ListHolidayCalendars").RequireAuthorization();
        app.MapGet("/holidaycalendars/{id:int}", GetHolidayCalendar).WithName("GetHolidayCalendar").RequireAuthorization();
        app.MapPut("/holidaycalendars/{id:int}", UpdateHolidayCalendar).WithName("UpdateHolidayCalendar")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
        app.MapDelete("/holidaycalendars/{id:int}", DeleteHolidayCalendar).WithName("DeleteHolidayCalendar")
            .RequireAuthorization(AuthorizationPolicies.ClientAdmin);
    }

    private static async Task<Results<Created<HolidayCalendarResponse>, ValidationProblem, ProblemHttpResult>> CreateHolidayCalendar(
        CreateHolidayCalendarRequest request, HolidayCalendarService service, CancellationToken ct)
    {
        var result = await service.CreateAsync(request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Created(
                $"/holidaycalendars/{result.Value!.Id}", HolidayCalendarResponse.FromEntity(result.Value)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for holiday calendar creation."),
        };
    }

    private static async Task<Ok<PagedResult<HolidayCalendarResponse>>> ListHolidayCalendars(
        int clientId, [AsParameters] PagingQuery paging, HolidayCalendarService service, CancellationToken ct)
    {
        var calendars = await service.ListAsync(clientId, paging, ct);
        return TypedResults.Ok(new PagedResult<HolidayCalendarResponse>
        {
            Items = calendars.Items.Select(HolidayCalendarResponse.FromEntity).ToList(),
            TotalCount = calendars.TotalCount,
            Page = calendars.Page,
            PageSize = calendars.PageSize,
        });
    }

    private static async Task<Results<Ok<HolidayCalendarResponse>, ProblemHttpResult>> GetHolidayCalendar(
        int id, HolidayCalendarService service, CancellationToken ct)
    {
        var result = await service.GetAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(HolidayCalendarResponse.FromEntity(result.Value!)),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for holiday calendar lookup."),
        };
    }

    private static async Task<Results<Ok<HolidayCalendarResponse>, ValidationProblem, ProblemHttpResult>> UpdateHolidayCalendar(
        int id, UpdateHolidayCalendarRequest request, HolidayCalendarService service, CancellationToken ct)
    {
        var result = await service.UpdateAsync(id, request, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.Ok(HolidayCalendarResponse.FromEntity(result.Value!)),
            ServiceResultKind.ValidationFailed => TypedResults.ValidationProblem(result.ValidationErrors!),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for holiday calendar update."),
        };
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> DeleteHolidayCalendar(
        int id, HolidayCalendarService service, CancellationToken ct)
    {
        var result = await service.DeleteAsync(id, ct);
        return result.Kind switch
        {
            ServiceResultKind.Success => TypedResults.NoContent(),
            ServiceResultKind.NotFound => TypedResults.Problem(
                detail: result.Detail, statusCode: StatusCodes.Status404NotFound),
            _ => throw new InvalidOperationException(
                $"Unexpected {nameof(ServiceResultKind)} '{result.Kind}' for holiday calendar deletion."),
        };
    }
}
