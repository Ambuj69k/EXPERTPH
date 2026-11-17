using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Branches, branch access, transfers with shortage, indents, per-branch books and branch comparison.</summary>
[Collection("api")]
public class Phase7Tests(ApiFixture f)
{
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private async Task<TenantClient> SwitchAsync(TenantClient c, Guid branchId)
    {
        var res = await c.Post("/api/v1/auth/switch-branch", new { branchId });
        var http = f.Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", res.GetProperty("token").GetString());
        return new TenantClient(http, f);
    }

    private static decimal Bal(JsonElement tb, string code) =>
        tb.GetProperty("rows").EnumerateArray().SingleOrDefault(r => r.GetProperty("code").GetString() == code) is { ValueKind: JsonValueKind.Object } r
            ? r.GetProperty("balance").GetDecimal() : 0m;

    [Fact]
    public async Task Transfer_with_shortage_indent_and_branch_books()
    {
        var main = await f.NewTenantAsync();
        var product = await main.CreateProductAsync("Multi Tab");
        var supplier = await main.CreateSupplierAsync();
        await main.PurchaseAsync(supplier, product, ("TR1", 12, 5, 100, 70)); // 50 tabs @ ₹7 at MAIN
        var wh = await main.Post("/api/v1/branches", new { name = "Central WH", code = "WH", stateCode = "27", isWarehouse = true, isActive = true });
        var whId = wh.GetProperty("id").GetGuid();
        var mainId = (await main.Get("/api/v1/me")).GetProperty("branchId").GetGuid();
        var batchId = (await main.Get("/api/v1/pos/search?q=Multi"))[0].GetProperty("batches")[0].GetProperty("batchId").GetGuid();

        var t = await main.Post("/api/v1/transfers", new { toBranchId = whId, lines = new[] { new { batchId, qtyUnits = 20 } } });
        Assert.StartsWith("TMAIN", t.GetProperty("transferNo").GetString());
        var tid = t.GetProperty("id").GetGuid();
        await main.Post($"/api/v1/transfers/{tid}/dispatch", new { });
        Assert.Equal(30m, await main.StockUnitsAsync(product));

        var atWh = await SwitchAsync(main, whId);
        var lineId = (await atWh.Get($"/api/v1/transfers/{tid}")).GetProperty("lines")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await main.Try(HttpMethod.Post, $"/api/v1/transfers/{tid}/receive", new { lines = new[] { new { lineId, receivedQtyUnits = 20 } } })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await atWh.Try(HttpMethod.Post, $"/api/v1/transfers/{tid}/receive", new { lines = new[] { new { lineId, receivedQtyUnits = 18 } } })).Status); // shortage needs a note
        Assert.Equal(HttpStatusCode.BadRequest, (await atWh.Try(HttpMethod.Post, $"/api/v1/transfers/{tid}/receive", new { lines = new[] { new { lineId, receivedQtyUnits = 21 } }, notes = "x" })).Status);
        var rec = await atWh.Post($"/api/v1/transfers/{tid}/receive", new { lines = new[] { new { lineId, receivedQtyUnits = 18 } }, notes = "2 tabs crushed" });
        Assert.Equal(14m, rec.GetProperty("shortageValue").GetDecimal());
        Assert.Equal(18m, await atWh.StockUnitsAsync(product));
        Assert.Equal(HttpStatusCode.Conflict, (await atWh.Try(HttpMethod.Post, $"/api/v1/transfers/{tid}/receive", new { lines = new[] { new { lineId, receivedQtyUnits = 18 } }, notes = "again" })).Status);
        Assert.Equal((HttpStatusCode.Conflict, "warehouse_no_billing"), await atWh.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 1, discountPercent = 0 } }, payments = new[] { new { mode = "Cash", amount = 10 } },
        }));

        var tbWh = await atWh.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tbWh.GetProperty("balanced").GetBoolean());
        Assert.Equal(126m, Bal(tbWh, "1200"));
        Assert.Equal(-140m, Bal(tbWh, "1210"));
        Assert.Equal(14m, Bal(tbWh, "5100"));
        var tbMain = await main.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.Equal(210m, Bal(tbMain, "1200"));
        Assert.Equal(140m, Bal(tbMain, "1210")); // nets to zero with the warehouse

        // store asks warehouse for 10 tabs; warehouse fulfils FEFO, store receives
        var indent = await main.Post("/api/v1/indents", new { supplyingBranchId = whId, lines = new[] { new { productId = product, qtyUnits = 10 } } });
        Assert.Equal(18m, indent.GetProperty("lines")[0].GetProperty("availableAtSupplier").GetDecimal());
        var back = await atWh.Post($"/api/v1/indents/{indent.GetProperty("id").GetGuid()}/fulfil", new { });
        Assert.Equal(mainId, back.GetProperty("toBranchId").GetGuid());
        await atWh.Post($"/api/v1/transfers/{back.GetProperty("id").GetGuid()}/dispatch", new { });
        var backLine = back.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await main.Post($"/api/v1/transfers/{back.GetProperty("id").GetGuid()}/receive", new { lines = new[] { new { lineId = backLine, receivedQtyUnits = 10 } } });
        Assert.Equal(40m, await main.StockUnitsAsync(product));
        Assert.Equal("Fulfilled", (await main.Get("/api/v1/indents"))[0].GetProperty("status").GetString());

        var cmp = await main.Get($"/api/v1/reports/branch-comparison?from={Today}&to={Today}");
        var rows = cmp.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(280m, rows.Single(r => r.GetProperty("branchId").GetGuid() == mainId).GetProperty("stockValue").GetDecimal());
        Assert.Equal(56m, rows.Single(r => r.GetProperty("branchId").GetGuid() == whId).GetProperty("stockValue").GetDecimal());
        Assert.Equal(336m, cmp.GetProperty("total").GetProperty("stockValue").GetDecimal());
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Staff_are_confined_to_their_branch()
    {
        var owner = await f.NewTenantAsync();
        var other = await owner.Post("/api/v1/branches", new { name = "Second Store", code = "S2", stateCode = "27", isWarehouse = false, isActive = true });
        var otherId = other.GetProperty("id").GetGuid();
        var cashier = await owner.AddUserAsync("Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Post, "/api/v1/auth/switch-branch", new { branchId = otherId })).Status);
        Assert.Single((await cashier.Get("/api/v1/branches")).EnumerateArray());
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, $"/api/v1/reports/branch-comparison?from={Today}&to={Today}")).Status);

        var users = await owner.Get("/api/v1/users");
        var cashierId = users.EnumerateArray().Single(u => u.GetProperty("role").GetString() == "Cashier").GetProperty("id").GetGuid();
        var assign = await owner.Http.PutAsJsonAsync($"/api/v1/users/{cashierId}/branch", new { branchId = otherId }, ApiFixture.Json);
        Assert.Equal(HttpStatusCode.NoContent, assign.StatusCode);
        // the cashier's token for the old branch stops working immediately
        Assert.Equal(HttpStatusCode.Unauthorized, (await cashier.Http.GetAsync("/api/v1/me")).StatusCode);
    }
}
