using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>SaaS provider console: separation from tenants, plans & limits, read-only states, support access, export, purge.</summary>
[Collection("api")]
public class Phase8Tests(ApiFixture f)
{
    private async Task<TenantClient> PlatformAsync()
    {
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json);
        await TenantClient.EnsureOk(res);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new TenantClient(http, f);
    }

    private static async Task<Guid> TenantId(TenantClient c) => (await c.Get("/api/v1/me")).GetProperty("tenantId").GetGuid();

    private static object Sub(string plan, string status, DateTimeOffset? trialEnds = null, string? extra = null) => new
    {
        planCode = plan, status, trialEndsAt = trialEnds, extraModules = extra, disabledModules = (string?)null,
        maxBranchesOverride = (int?)null, maxUsersOverride = (int?)null, migration = "InProgress", notes = "test",
    };

    [Fact]
    public async Task Provider_and_tenant_tokens_are_separated()
    {
        var platform = await PlatformAsync();
        var tenant = await f.NewTenantAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.Try(HttpMethod.Get, "/api/v1/products")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await tenant.Try(HttpMethod.Get, "/api/v1/platform/dashboard")).Status);
        Assert.True((await platform.Get("/api/v1/platform/dashboard")).GetProperty("tenants").GetInt32() >= 1);
        var bad = await f.Factory.CreateClient().PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "wrong" }, ApiFixture.Json);
        Assert.Equal(HttpStatusCode.Unauthorized, bad.StatusCode);
    }

    [Fact]
    public async Task Plan_modules_limits_and_read_only_states()
    {
        var platform = await PlatformAsync();
        var c = await f.NewTenantAsync();
        var tid = await TenantId(c);
        var sub = (await c.Get("/api/v1/subscription")).GetProperty("current");
        Assert.Equal("Trial", sub.GetProperty("status").GetString());
        Assert.Contains("accounting", sub.GetProperty("modules").EnumerateArray().Select(m => m.GetString()));

        await platform.Put($"/api/v1/platform/tenants/{tid}/subscription", Sub("single", "Active"));
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, $"/api/v1/accounting/trial-balance?asOf={DateTime.Today:yyyy-MM-dd}"));
        Assert.Equal((HttpStatusCode.Conflict, "plan_limit"), await c.Try(HttpMethod.Post, "/api/v1/branches", new { name = "Two", code = "TWO", stateCode = "27", isWarehouse = false, isActive = true }));
        await platform.Put($"/api/v1/platform/tenants/{tid}/subscription", Sub("single", "Active", extra: "accounting"));
        Assert.True((await c.Get($"/api/v1/accounting/trial-balance?asOf={DateTime.Today:yyyy-MM-dd}")).GetProperty("balanced").GetBoolean());

        // trial ended → read-only, but reading, export and support still work
        await platform.Put($"/api/v1/platform/tenants/{tid}/subscription", Sub("chain", "Trial", DateTimeOffset.UtcNow.AddDays(-1)));
        Assert.Equal((HttpStatusCode.PaymentRequired, "subscription_inactive"), await c.Try(HttpMethod.Post, "/api/v1/suppliers", new { name = "X", paymentTermsDays = 0, isActive = true }));
        await c.Get("/api/v1/products");
        Assert.Equal(HttpStatusCode.OK, (await c.Http.GetAsync("/api/v1/export/all.zip")).StatusCode);
        await c.Post("/api/v1/support/tickets", new { subject = "Trial ended", body = "Please activate" });

        // provider bills and records payment → active again
        var inv = await platform.Post($"/api/v1/platform/tenants/{tid}/invoices", new
        {
            periodFrom = DateTime.Today.ToString("yyyy-MM-dd"), periodTo = DateTime.Today.AddMonths(1).ToString("yyyy-MM-dd"), amount = 2499,
            dueDate = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd"),
        });
        Assert.Contains((await platform.Get("/api/v1/platform/invoices/overdue")).EnumerateArray(), i => i.GetProperty("id").GetGuid() == inv.GetProperty("id").GetGuid());
        await platform.Post($"/api/v1/platform/invoices/{inv.GetProperty("id").GetGuid()}/paid", new { reference = "NEFT-77" });
        Assert.Equal("Active", (await c.Get("/api/v1/subscription")).GetProperty("current").GetProperty("status").GetString());
        await c.Post("/api/v1/suppliers", new { name = "Now allowed", paymentTermsDays = 0, isActive = true });

        // suspension cuts off existing sessions immediately; reactivation restores them
        await platform.Post($"/api/v1/platform/tenants/{tid}/suspend", new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.Http.GetAsync("/api/v1/products")).StatusCode);
        await platform.Post($"/api/v1/platform/tenants/{tid}/reactivate", new { });
        Assert.Equal(HttpStatusCode.OK, (await c.Http.GetAsync("/api/v1/products")).StatusCode);
    }

    [Fact]
    public async Task Support_access_needs_owner_grant_is_read_only_and_audited()
    {
        var platform = await PlatformAsync();
        var c = await f.NewTenantAsync();
        var tid = await TenantId(c);
        Assert.Equal(HttpStatusCode.Forbidden, (await platform.Try(HttpMethod.Post, $"/api/v1/platform/tenants/{tid}/support-session")).Status);

        await c.Put("/api/v1/settings/support-access", new { hours = 2 });
        var session = await platform.Post($"/api/v1/platform/tenants/{tid}/support-session", new { });
        var support = f.Factory.CreateClient();
        support.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.GetProperty("token").GetString());
        var s = new TenantClient(support, f);
        await s.Get("/api/v1/products");
        Assert.Equal((HttpStatusCode.Forbidden, "support_read_only"), await s.Try(HttpMethod.Post, "/api/v1/suppliers", new { name = "hack", paymentTermsDays = 0, isActive = true }));

        var detail = await platform.Get($"/api/v1/platform/tenants/{tid}");
        Assert.Contains(detail.GetProperty("recentAudit").EnumerateArray(), a => a.GetProperty("action").GetString() == "support.request");

        await c.Put("/api/v1/settings/support-access", new { hours = 0 }); // owner revokes → token dies immediately
        Assert.Equal(HttpStatusCode.Unauthorized, (await support.GetAsync("/api/v1/products")).StatusCode);

        // ticket round-trip
        var ticket = await c.Post("/api/v1/support/tickets", new { subject = "Printer", body = "Bill not printing", priority = "high" });
        Assert.Contains((await platform.Get("/api/v1/platform/tickets")).EnumerateArray(), t => t.GetProperty("id").GetGuid() == ticket.GetProperty("id").GetGuid());
        await platform.Post($"/api/v1/platform/tickets/{ticket.GetProperty("id").GetGuid()}/reply", new { body = "Check driver", status = "WaitingOnCustomer" });
        var mine = (await c.Get("/api/v1/support/tickets"))[0];
        Assert.Equal("WaitingOnCustomer", mine.GetProperty("status").GetString());
        Assert.True(mine.GetProperty("messages")[1].GetProperty("fromProvider").GetBoolean());
    }

    [Fact]
    public async Task Owner_export_contains_business_data()
    {
        var c = await f.NewTenantAsync();
        await c.CreateProductAsync("Exportable Syrup");
        var zipBytes = await c.Http.GetByteArrayAsync("/api/v1/export/all.zip");
        using var zip = new ZipArchive(new MemoryStream(zipBytes));
        Assert.Contains(zip.Entries, e => e.Name == "sales_invoices.csv");
        using var reader = new StreamReader(zip.GetEntry("products.csv")!.Open());
        Assert.Contains("Exportable Syrup", await reader.ReadToEndAsync());
        var cashier = await c.AddUserAsync("Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Http.GetAsync("/api/v1/export/all.zip")).StatusCode);
    }

    [Fact]
    public async Task Purge_removes_one_tenant_and_leaves_others_intact()
    {
        var platform = await PlatformAsync();
        var a = await f.NewTenantAsync();
        var b = await f.NewTenantAsync();
        foreach (var c in new[] { a, b })
        {
            var p = await c.CreateProductAsync("Purge Tab");
            var s = await c.CreateSupplierAsync();
            await c.PurchaseAsync(s, p, ("PG1", 12, 3, 100, 70));
            await c.SellAsync(p, 5, 50);
            await c.Get($"/api/v1/accounting/trial-balance?asOf={DateTime.Today:yyyy-MM-dd}"); // journals exist too
        }
        var aid = await TenantId(a);
        var bProduct = (await b.Get("/api/v1/products")).GetProperty("items")[0].GetProperty("id").GetGuid();

        Assert.Equal((HttpStatusCode.Conflict, "not_scheduled"), await platform.Try(HttpMethod.Post, $"/api/v1/platform/tenants/{aid}/purge"));
        await platform.Post($"/api/v1/platform/tenants/{aid}/schedule-deletion", new { daysFromNow = 0 });
        Assert.Equal(HttpStatusCode.PaymentRequired, (await a.Try(HttpMethod.Post, "/api/v1/suppliers", new { name = "late", paymentTermsDays = 0, isActive = true })).Status);
        await platform.Post($"/api/v1/platform/tenants/{aid}/purge", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Http.GetAsync("/api/v1/me")).StatusCode);
        var tenants = await platform.Get("/api/v1/platform/tenants");
        Assert.Equal("Deleted tenant", tenants.EnumerateArray().Single(t => t.GetProperty("id").GetGuid() == aid).GetProperty("name").GetString());

        Assert.Equal(25m, await b.StockUnitsAsync(bProduct)); // other tenant untouched
        await b.SellAsync(bProduct, 1, 10);
        Assert.True((await b.Get($"/api/v1/accounting/trial-balance?asOf={DateTime.Today:yyyy-MM-dd}")).GetProperty("balanced").GetBoolean());
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
