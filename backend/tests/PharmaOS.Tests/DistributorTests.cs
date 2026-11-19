using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Distributor (B2B): rates, schemes, salesman orders, FEFO trade invoices, IGST, licence & credit control, collections, credit notes, books.</summary>
[Collection("api")]
public class DistributorTests(ApiFixture f)
{
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private async Task SetPlan(Guid tenantId, string plan)
    {
        var p = f.Factory.CreateClient();
        var login = await (await p.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        p.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());
        await TenantClient.EnsureOk(await p.PutAsJsonAsync($"/api/v1/platform/tenants/{tenantId}/subscription", new { planCode = plan, status = "Active", migration = "Completed" }, ApiFixture.Json));
    }

    private static object Retailer(string name, string state, string? gstin, DateTime? licenceTo, decimal limit, Guid? salesman = null, int? version = null) => new
    {
        name, gstin, stateCode = state, drugLicenseNo = "DL-" + name, drugLicenseValidTo = licenceTo?.ToString("yyyy-MM-dd"), phone = "9800000000", address = "Market",
        route = "R1", creditLimit = limit, creditDays = 30, salesmanUserId = salesman, isActive = true, version,
    };

    [Fact]
    public async Task Distributor_end_to_end()
    {
        var c = await f.NewTenantAsync();
        var me = await c.Get("/api/v1/me");
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, "/api/v1/distribution/retailers"));
        await SetPlan(me.GetProperty("tenantId").GetGuid(), "distributor");

        var product = await c.CreateProductAsync("Trade Tab"); // strip of 10, GST 12
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("TB1", 12, 100, 100, 70)); // 100 strips

        var salesman = await c.AddUserAsync("Salesman");
        var salesmanId = (await c.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("role").GetString() == "Salesman").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/distribution/retailers", Retailer("Bad", "27", "29ABCDE1234F1Z5", null, 0))).Status); // GSTIN state mismatch
        var r1 = (await c.Post("/api/v1/distribution/retailers", Retailer("City Chemist", "27", "27ABCDE1234F1Z5", DateTime.Today.AddYears(1), 5000, salesmanId))).GetProperty("id").GetGuid();
        var r2 = await c.Post("/api/v1/distribution/retailers", Retailer("Border Pharma", "29", "29ABCDE1234F1Z6", DateTime.Today.AddDays(-1), 0));
        var r2Id = r2.GetProperty("id").GetGuid();

        await c.Put("/api/v1/distribution/rates", new { productId = product, retailerId = (Guid?)null, ratePerPack = 80 });
        await c.Put("/api/v1/distribution/rates", new { productId = product, retailerId = r1, ratePerPack = 78 });
        await c.Post("/api/v1/distribution/schemes", new { productId = product, name = "10+1", buyQtyPacks = 10, freeQtyPacks = 1, discountPercent = 2, validFrom = Today, validTo = DateTime.Today.AddMonths(1).ToString("yyyy-MM-dd"), isActive = true });

        // salesman sees only their retailers and books an order
        var seen = await salesman.Get("/api/v1/distribution/retailers");
        Assert.Equal("City Chemist", Assert.Single(seen.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await salesman.Try(HttpMethod.Post, "/api/v1/distribution/orders", new { retailerId = r2Id, lines = new[] { new { productId = product, qtyPacks = 1 } } })).Status);
        var order = await salesman.Post("/api/v1/distribution/orders", new { retailerId = r1, lines = new[] { new { productId = product, qtyPacks = 20 } } });
        Assert.Equal(HttpStatusCode.Forbidden, (await salesman.Try(HttpMethod.Post, $"/api/v1/distribution/orders/{order.GetProperty("id").GetGuid()}/invoice", new { })).Status);

        var inv1 = await c.Post($"/api/v1/distribution/orders/{order.GetProperty("id").GetGuid()}/invoice", new { });
        var l1 = Assert.Single(inv1.GetProperty("lines").EnumerateArray());
        Assert.Equal(20m, l1.GetProperty("qtyPacks").GetDecimal());
        Assert.Equal(2m, l1.GetProperty("freeQtyPacks").GetDecimal());   // 10+1 scheme
        Assert.Equal(78m, l1.GetProperty("ratePerPack").GetDecimal());   // retailer-specific rate wins
        Assert.Equal(1528.80m, inv1.GetProperty("taxableAmount").GetDecimal());
        Assert.Equal(183.46m, inv1.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(1712m, inv1.GetProperty("netAmount").GetDecimal());
        Assert.False(inv1.GetProperty("interState").GetBoolean());
        Assert.Equal(780m, await c.StockUnitsAsync(product)); // 1000 − 22 packs × 10
        Assert.Equal("Invoiced", (await c.Get($"/api/v1/distribution/orders/{order.GetProperty("id").GetGuid()}")).GetProperty("status").GetString());

        // other state, expired licence → blocked; after licence update → IGST invoice at default rate
        object Inv(Guid retailer, decimal packs, string? overrideReason = null) => new { clientRequestId = Guid.NewGuid(), retailerId = retailer, lines = new[] { new { productId = product, qtyPacks = packs } }, creditOverrideReason = overrideReason };
        Assert.Equal((HttpStatusCode.Conflict, "licence_expired"), await c.Try(HttpMethod.Post, "/api/v1/distribution/invoices", Inv(r2Id, 5)));
        await c.Put($"/api/v1/distribution/retailers/{r2Id}", Retailer("Border Pharma", "29", "29ABCDE1234F1Z6", DateTime.Today.AddYears(2), 0, version: r2.GetProperty("version").GetInt32()));
        var inv2 = await c.Post("/api/v1/distribution/invoices", Inv(r2Id, 5));
        Assert.True(inv2.GetProperty("interState").GetBoolean());
        Assert.Equal(439m, inv2.GetProperty("netAmount").GetDecimal()); // 5 × 80 − 2% = 392 + 12% = 439.04

        // credit control on the ₹5000 limit
        Assert.Equal(2568m, (await c.Post("/api/v1/distribution/invoices", Inv(r1, 30))).GetProperty("netAmount").GetDecimal()); // due 4280
        Assert.Equal((HttpStatusCode.Conflict, "credit_blocked"), await c.Try(HttpMethod.Post, "/api/v1/distribution/invoices", Inv(r1, 10)));
        var pharmacist = await c.AddUserAsync("Pharmacist");
        Assert.Equal(HttpStatusCode.Forbidden, (await pharmacist.Try(HttpMethod.Post, "/api/v1/distribution/invoices", Inv(r1, 10, "urgent"))).Status);
        Assert.Equal(856m, (await c.Post("/api/v1/distribution/invoices", Inv(r1, 10, "Long-standing customer, cheque promised"))).GetProperty("netAmount").GetDecimal());

        // salesman collects; credit note for saleable returns
        var rc = await salesman.Post("/api/v1/distribution/receipts", new { retailerId = r1, mode = "Cash", amount = 1000 });
        Assert.Equal(1000m, rc.GetProperty("allocated").GetDecimal());
        var cn = await c.Post("/api/v1/distribution/credit-notes", new
        {
            clientRequestId = Guid.NewGuid(), tradeInvoiceId = inv1.GetProperty("id").GetGuid(), reason = "Saleable",
            lines = new[] { new { tradeInvoiceLineId = l1.GetProperty("id").GetGuid(), qtyPacks = 2 } },
        });
        Assert.Equal(171.23m, cn.GetProperty("amount").GetDecimal()); // 152.88 + 18.35
        var st = await c.Get($"/api/v1/distribution/retailers/{r1}/statement");
        Assert.Equal(3964.77m, st.GetProperty("outstanding").GetDecimal()); // 1712 + 2568 + 856 − 171.23 − 1000

        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        foreach (var chk in (await c.Get("/api/v1/accounting/control-checks")).EnumerateArray())
            Assert.True(chk.GetProperty("difference").GetDecimal() == 0, $"{chk.GetProperty("name").GetString()} off by {chk.GetProperty("difference")}");
        var sm = await c.Get($"/api/v1/distribution/reports/salesmen?from={Today}&to={Today}");
        Assert.Equal(1000m, sm.EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Salesman").GetProperty("collections").GetDecimal());
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
