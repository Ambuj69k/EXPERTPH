using System.Net;

namespace PharmaOS.Tests;

[Collection("api")]
public class BranchPriceTests(ApiFixture f)
{
    private static object Sale(Guid product, decimal units, decimal pay, decimal discount = 0, Guid? batchId = null) => new
    {
        clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, batchId, qtyUnits = units, discountPercent = discount } },
        payments = new[] { new { mode = "Cash", amount = pay } },
    };

    [Fact]
    public async Task Branch_price_below_mrp_is_used_never_above_mrp()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Priced Tab"); // strip of 10
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("P100", 12, 5, 100, 70), ("P80", 18, 2, 80, 50)); // two MRPs

        await c.Put($"/api/v1/prices/{product}", new { pricePerPack = 90 });
        var hit = (await c.Get("/api/v1/pos/search?q=Priced"))[0];
        Assert.Equal(90m, hit.GetProperty("branchPricePerPack").GetDecimal());

        var s1 = await c.Post("/api/v1/sales", Sale(product, 10, 90)); // FEFO picks MRP-100 batch
        var l1 = s1.GetProperty("lines")[0];
        Assert.Equal(100m, l1.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(10m, l1.GetProperty("discountAmount").GetDecimal());
        Assert.Equal(10m, l1.GetProperty("discountPercent").GetDecimal());

        var cashier = await c.AddUserAsync("Cashier");
        var s2 = await cashier.Post("/api/v1/sales", Sale(product, 10, 86, discount: 5)); // 90 − 5% = 85.50 → 86; cap checks only the extra 5%
        Assert.Equal(86m, s2.GetProperty("netAmount").GetDecimal());
        Assert.Equal(14.5m, s2.GetProperty("lines")[0].GetProperty("discountPercent").GetDecimal());

        var p80 = (await c.Get("/api/v1/pos/search?q=Priced"))[0].GetProperty("batches").EnumerateArray().Single(b => b.GetProperty("mrp").GetDecimal() == 80m).GetProperty("batchId").GetGuid();
        Assert.Equal(80m, (await c.Post("/api/v1/sales", Sale(product, 10, 80, batchId: p80))).GetProperty("netAmount").GetDecimal()); // MRP 80 < price 90

        await c.Put($"/api/v1/prices/{product}", new { pricePerPack = 120 }); // above MRP → MRP applies
        Assert.Equal(100m, (await c.Post("/api/v1/sales", Sale(product, 10, 100))).GetProperty("netAmount").GetDecimal());

        await c.Put($"/api/v1/prices/{product}", new { pricePerPack = (decimal?)null });
        Assert.Equal(System.Text.Json.JsonValueKind.Null, (await c.Get("/api/v1/pos/search?q=Priced"))[0].GetProperty("branchPricePerPack").ValueKind);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Put, $"/api/v1/prices/{product}", new { pricePerPack = 1 })).Status);
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
