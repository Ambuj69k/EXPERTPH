using System.Net;

namespace PharmaOS.Tests;

/// <summary>Offline POS: catalogue snapshot and late-synced bills (capture time kept, idempotent, bounded window, stock re-checked).</summary>
[Collection("api")]
public class Phase9Tests(ApiFixture f)
{
    private static object OfflineSale(Guid product, decimal units, decimal amount, DateTimeOffset capturedAt, Guid? id = null, string? offlineRef = "DEV1-0001") => new
    {
        clientRequestId = id ?? Guid.NewGuid(), capturedAt, offlineRef,
        lines = new[] { new { productId = product, qtyUnits = units, discountPercent = 0 } },
        payments = new[] { new { mode = "Cash", amount } },
    };

    [Fact]
    public async Task Catalog_snapshot_and_offline_bill_sync()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Offline Tab");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("OF1", 12, 2, 100, 70)); // 20 tabs

        var catalog = await c.Get("/api/v1/pos/catalog");
        var item = Assert.Single(catalog.EnumerateArray());
        Assert.Equal(20m, item.GetProperty("availableUnits").GetDecimal());

        var captured = DateTimeOffset.UtcNow.AddHours(-2);
        var key = Guid.NewGuid();
        var synced = await c.Post("/api/v1/sales", OfflineSale(product, 5, 50, captured, key));
        Assert.True(synced.GetProperty("capturedOffline").GetBoolean());
        Assert.Equal("DEV1-0001", synced.GetProperty("offlineRef").GetString());
        Assert.Equal(captured.ToUnixTimeSeconds(), synced.GetProperty("invoiceAt").GetDateTimeOffset().ToUnixTimeSeconds());
        var again = await c.Post("/api/v1/sales", OfflineSale(product, 5, 50, captured, key)); // sync retried after a dropped response
        Assert.Equal(synced.GetProperty("invoiceNo").GetString(), again.GetProperty("invoiceNo").GetString());
        Assert.Equal(15m, await c.StockUnitsAsync(product));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/sales", OfflineSale(product, 1, 10, DateTimeOffset.UtcNow.AddDays(-4)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/sales", OfflineSale(product, 1, 10, DateTimeOffset.UtcNow.AddHours(1)))).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/sales", OfflineSale(product, 1, 10, DateTimeOffset.UtcNow.AddMinutes(-5), offlineRef: null))).Status);
        // stock sold elsewhere meanwhile → the offline bill is refused at sync and must be resolved by staff
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_stock"), await c.Try(HttpMethod.Post, "/api/v1/sales", OfflineSale(product, 16, 160, DateTimeOffset.UtcNow.AddMinutes(-30))));
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
