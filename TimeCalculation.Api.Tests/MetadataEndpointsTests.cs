using System.Net;
using System.Net.Http.Json;
using TimeCalculation.Api.Contracts;
using Xunit;

namespace TimeCalculation.Api.Tests;

[Collection("Api")]
public class MetadataEndpointsTests(ApiFixture fixture)
{
    [Fact]
    public async Task GetPremiumRules_ReturnsAllSixRegisteredRules()
    {
        var response = await fixture.SystemAdminClient.GetAsync("/metadata/premium-rules", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rules = await response.Content.ReadFromJsonAsync<List<PremiumRuleMetadataResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(rules);
        Assert.Equal(6, rules.Count);
        Assert.Contains(rules, r => r.Code == "CA_MEAL");
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }

    [Fact]
    public async Task GetPayRuleTemplates_ReturnsAllSixTemplates()
    {
        var response = await fixture.SystemAdminClient.GetAsync("/metadata/pay-rule-templates", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var templates = await response.Content.ReadFromJsonAsync<List<PayRuleTemplateResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(templates);
        Assert.Equal(6, templates.Count);
        Assert.Contains(templates, t => t.Code == "federal-standard");
        var california = templates.Single(t => t.Code == "california");
        Assert.Contains("CA_MEAL", california.ActivePremiumCodes);
        Assert.Contains("CA_REST", california.ActivePremiumCodes);
        Assert.True(california.HasDailyOvertime);
        Assert.True(california.HasSeventhDayRule);
        Assert.All(templates, t => Assert.False(string.IsNullOrWhiteSpace(t.Name)));
        Assert.All(templates, t => Assert.False(string.IsNullOrWhiteSpace(t.Description)));
    }
}
