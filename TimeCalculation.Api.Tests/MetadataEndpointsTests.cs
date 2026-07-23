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
        var response = await fixture.Client.GetAsync("/metadata/premium-rules", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rules = await response.Content.ReadFromJsonAsync<List<PremiumRuleMetadataResponse>>(TestJson.Options, TestContext.Current.CancellationToken);
        Assert.NotNull(rules);
        Assert.Equal(6, rules.Count);
        Assert.Contains(rules, r => r.Code == "CA_MEAL");
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Name)));
        Assert.All(rules, r => Assert.False(string.IsNullOrWhiteSpace(r.Description)));
    }
}
