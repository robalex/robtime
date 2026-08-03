using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using TimeCalculation.Model;
using TimeCalculation.Persistence;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class PayrollEarningCodeMappingEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task CreateMapping_AsEmployee_Returns403()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);
        var employeeApi = fixture.CreateAuthenticatedClient(AppRole.Employee, clientId, sub: $"test-employee-{Guid.NewGuid()}");

        var response = await employeeApi.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Regular, LineCode = "", EarningCode = "REG", ValueBasis = PayrollExportValueBasis.Hours,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateMapping_ForNonexistentProfile_Returns404()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);

        var response = await api.PostAsJsonAsync(
            "/payroll-export-profiles/999999999/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Regular, LineCode = "", EarningCode = "REG", ValueBasis = PayrollExportValueBasis.Hours,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(PayLineType.Regular, "NOT_EMPTY")]        // Regular must be ""
    [InlineData(PayLineType.OvertimePremium, "HALFTIME")] // must be OVERTIME or DOUBLETIME
    [InlineData(PayLineType.Bonus, "Whatever")]            // must be a real BonusKind name
    [InlineData(PayLineType.Differential, "")]             // Differential needs a real rule code
    public async Task CreateMapping_LineCodeShapeWrongForType_Returns400(PayLineType lineType, string lineCode)
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = lineType, LineCode = lineCode, EarningCode = "CODE", ValueBasis = PayrollExportValueBasis.Amount,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(PayLineType.OvertimePremium, "OVERTIME")]
    [InlineData(PayLineType.Premium, "CA_MEAL")]
    [InlineData(PayLineType.Bonus, "NonDiscretionary")]
    public async Task CreateMapping_RateDerivedTypeWithHoursBasis_Returns400(PayLineType lineType, string lineCode)
    {
        // OvertimePremium/Premium are priced off the weighted regular rate, and Bonus lines always
        // carry zero hours — mapping any of the three as Hours would export nothing or the wrong
        // amount, so the validator refuses it outright rather than accepting a silently-wrong config.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var response = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = lineType, LineCode = lineCode, EarningCode = "CODE", ValueBasis = PayrollExportValueBasis.Hours,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FullLifecycle_CreateListPutDelete_BehavesCorrectly()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var createResponse = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "DIFF1",
                ValueBasis = PayrollExportValueBasis.Amount, Description = "Night shift differential",
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = (await createResponse.Content.ReadFromJsonAsync<PayrollEarningCodeMappingResponse>(
            TestJson.Options, TestContext.Current.CancellationToken))!;

        var listResponse = await api.GetAsync($"/payroll-export-profiles/{profileId}/earning-codes", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await listResponse.Content.ReadFromJsonAsync<List<PayrollEarningCodeMappingResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Contains(list!, m => m.Id == created.Id);

        var updateResponse = await api.PutAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes/{created.Id}",
            new UpdatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "DIFF2",
                ValueBasis = PayrollExportValueBasis.Amount,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await updateResponse.Content.ReadFromJsonAsync<PayrollEarningCodeMappingResponse>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.Equal("DIFF2", updated!.EarningCode);

        var deleteResponse = await api.DeleteAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes/{created.Id}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listAfterDelete = await api.GetAsync($"/payroll-export-profiles/{profileId}/earning-codes", TestContext.Current.CancellationToken);
        var listAfter = await listAfterDelete.Content.ReadFromJsonAsync<List<PayrollEarningCodeMappingResponse>>(
            TestJson.Options, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(listAfter!, m => m.Id == created.Id);
    }

    [Fact]
    public async Task CreateMapping_DuplicateLineKeyOnSameProfile_Returns409()
    {
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId);

        var first = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "DIFF1",
                ValueBasis = PayrollExportValueBasis.Amount,
            },
            TestJson.Options, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var duplicate = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileId}/earning-codes",
            new CreatePayrollEarningCodeMappingRequest
            {
                LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "SOME_OTHER_CODE",
                ValueBasis = PayrollExportValueBasis.Amount,
            },
            TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
    }

    [Fact]
    public async Task CreateMapping_SameLineKeyOnDifferentProfiles_DoesNotConflict()
    {
        // Uniqueness is scoped per profile, not per client — two profiles for the same client (e.g.
        // a dual-running cutover) can each map SHIFT_DIFF independently.
        var (clientId, api) = await fixture.CreateClientAndScopedClientAsync(
            $"Payroll Test Co {Guid.NewGuid()}", TestContext.Current.CancellationToken);
        var profileAId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId, "Old Provider");
        var profileBId = await PayrollExportProfileEndpointsTests.CreateProfileAsync(api, clientId, "New Provider");

        var requestA = new CreatePayrollEarningCodeMappingRequest
        {
            LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "DIFF1",
            ValueBasis = PayrollExportValueBasis.Amount,
        };
        var requestB = new CreatePayrollEarningCodeMappingRequest
        {
            LineType = PayLineType.Differential, LineCode = "SHIFT_DIFF", EarningCode = "SHIFTPAY",
            ValueBasis = PayrollExportValueBasis.Amount,
        };

        var responseA = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileAId}/earning-codes", requestA, TestJson.Options, TestContext.Current.CancellationToken);
        var responseB = await api.PostAsJsonAsync(
            $"/payroll-export-profiles/{profileBId}/earning-codes", requestB, TestJson.Options, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, responseA.StatusCode);
        Assert.Equal(HttpStatusCode.Created, responseB.StatusCode);
    }
}
