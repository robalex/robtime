using TimeCalculation.Model;
using TimeCalculation.Persistence;

namespace TimeCalculation.Api.Contracts;

/// <summary>No ProfileId — route-identified, same as PositionAssignment's requests being scoped
/// under /employees/{employeeId}. A mapping has no meaning apart from the profile it's for.</summary>
public record CreatePayrollEarningCodeMappingRequest
{
    public required PayLineType LineType { get; init; }
    public required string LineCode { get; init; }
    public required string EarningCode { get; init; }
    public required PayrollExportValueBasis ValueBasis { get; init; }
    public string? Description { get; init; }
}

public record UpdatePayrollEarningCodeMappingRequest
{
    public required PayLineType LineType { get; init; }
    public required string LineCode { get; init; }
    public required string EarningCode { get; init; }
    public required PayrollExportValueBasis ValueBasis { get; init; }
    public string? Description { get; init; }
}

public sealed record PayrollEarningCodeMappingResponse
{
    public required int Id { get; init; }
    public required int ProfileId { get; init; }
    public required PayLineType LineType { get; init; }
    public required string LineCode { get; init; }
    public required string EarningCode { get; init; }
    public required PayrollExportValueBasis ValueBasis { get; init; }
    public required string Description { get; init; }

    public static PayrollEarningCodeMappingResponse FromEntity(PayrollEarningCodeMapping mapping) => new()
    {
        Id = mapping.Id,
        ProfileId = mapping.ProfileId,
        LineType = mapping.LineType,
        LineCode = mapping.LineCode,
        EarningCode = mapping.EarningCode,
        ValueBasis = mapping.ValueBasis,
        Description = mapping.Description,
    };
}
