using System.Net;

namespace PharmaOS.Tests;

/// <summary>Races found by the browser tests: several requests touching a tenant's books for the first time at once.</summary>
[Collection("api")]
public class ConcurrencyTests(ApiFixture f)
{
    [Fact]
    public async Task First_visit_to_accounts_with_parallel_requests_never_fails()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Race Tab");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("RC1", 12, 2, 100, 70));
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var calls = Enumerable.Range(0, 6).Select(i => (i % 3) switch
        {
            0 => c.Http.PostAsync("/api/v1/accounting/sync", new StringContent("{}", System.Text.Encoding.UTF8, "application/json")),
            1 => c.Http.GetAsync("/api/v1/accounting/control-checks"),
            _ => c.Http.GetAsync($"/api/v1/accounting/trial-balance?asOf={today}"),
        });
        var results = await Task.WhenAll(calls);
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        Assert.Equal(157m, tb.GetProperty("totalDebit").GetDecimal()); // purchase posted exactly once: 140 + 16.80 GST + 0.20 round-off
    }
}
