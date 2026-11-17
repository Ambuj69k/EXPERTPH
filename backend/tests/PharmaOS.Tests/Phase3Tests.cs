using System.Net;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Cash shifts, stock counts, batch quarantine and recall trace.</summary>
[Collection("api")]
public class Phase3Tests(ApiFixture f)
{
    private async Task<(TenantClient C, Guid Product)> StockedAsync()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Counter Tab");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("NEAR", 3, 2, 100, 70), ("FAR", 12, 3, 100, 70)); // 20 + 30 units, ₹10/tab, cost ₹7
        return (c, product);
    }

    private static object Sale(Guid product, decimal units, params (string Mode, decimal Amount)[] pays) => new
    {
        clientRequestId = Guid.NewGuid(),
        lines = new[] { new { productId = product, qtyUnits = units, discountPercent = 0 } },
        payments = pays.Select(p => new { mode = p.Mode, amount = p.Amount, reference = p.Mode == "Upi" ? "UTR" : null }).ToArray(),
    };

    [Fact]
    public async Task Shift_expected_cash_and_variance()
    {
        var (c, product) = await StockedAsync();
        await c.Put("/api/v1/settings", new { cashierMaxDiscountPercent = 10, requireOpenShift = true });
        Assert.Equal((HttpStatusCode.Conflict, "shift_required"), await c.Try(HttpMethod.Post, "/api/v1/sales", Sale(product, 1, ("Cash", 10))));

        await c.Post("/api/v1/shifts/open", new { openingCash = 500 });
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, "/api/v1/shifts/open", new { openingCash = 1 })).Status);

        var a = await c.Post("/api/v1/sales", Sale(product, 10, ("Cash", 100)));
        await c.Post("/api/v1/sales", Sale(product, 5, ("Upi", 50)));
        var cs = await c.Post("/api/v1/sales", Sale(product, 3, ("Cash", 30)));
        await c.Post($"/api/v1/sales/{cs.GetProperty("id").GetGuid()}/cancel", new { reason = "Customer left" });
        await c.Post("/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = a.GetProperty("id").GetGuid(), reason = "Excess", refundMode = "Cash",
            lines = new[] { new { salesInvoiceLineId = a.GetProperty("lines")[0].GetProperty("id").GetGuid(), qtyUnits = 2 } },
        });
        await c.Post("/api/v1/shifts/current/movements", new { kind = "PayOut", amount = 15, reason = "Tea" });

        var cur = await c.Get("/api/v1/shifts/current");
        Assert.Equal(565m, cur.GetProperty("expectedCash").GetDecimal()); // 500 + 130 − 30 − 20 − 15
        Assert.Equal(50m, cur.GetProperty("upiSales").GetDecimal());
        Assert.Equal(3, cur.GetProperty("billCount").GetInt32());

        var closed = await c.Post("/api/v1/shifts/current/close", new { countedCash = 560, notes = "short 5" });
        Assert.Equal("Closed", closed.GetProperty("status").GetString());
        Assert.Equal(-5m, closed.GetProperty("variance").GetDecimal());
        Assert.Equal(HttpStatusCode.NoContent, (await c.Http.GetAsync("/api/v1/shifts/current")).StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "shift_required"), await c.Try(HttpMethod.Post, "/api/v1/sales", Sale(product, 1, ("Cash", 10))));

        var list = await c.Get("/api/v1/shifts");
        Assert.Equal(-5m, Assert.Single(list.EnumerateArray()).GetProperty("variance").GetDecimal());
    }

    [Fact]
    public async Task Stock_count_uses_system_qty_at_entry_time()
    {
        var (c, product) = await StockedAsync();
        var count = await c.Post("/api/v1/stock-counts", new { title = "Rack A" });
        var lines = count.GetProperty("lines").EnumerateArray().ToList();
        Guid Line(string batch) => lines.Single(l => l.GetProperty("batchNo").GetString() == batch).GetProperty("id").GetGuid();
        var id = count.GetProperty("id").GetGuid();

        await c.Put($"/api/v1/stock-counts/{id}/entries", new[] { new { lineId = Line("NEAR"), countedQty = 18 }, new { lineId = Line("FAR"), countedQty = 30 } });
        await c.SellAsync(product, 5, 50); // after counting: FEFO takes 5 from NEAR (system 20 → 15, shelf 18 → 13)

        var posted = await c.Post($"/api/v1/stock-counts/{id}/post", new { });
        Assert.Equal("Posted", posted.GetProperty("status").GetString());
        Assert.Equal(14m, posted.GetProperty("lossValue").GetDecimal()); // 2 tabs × ₹7
        Assert.Equal(43m, await c.StockUnitsAsync(product)); // 13 + 30, not 11 + 30
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, $"/api/v1/stock-counts/{id}/post", new { })).Status);
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Blocked_batch_is_skipped_by_FEFO_and_trace_shows_customers()
    {
        var (c, product) = await StockedAsync();
        await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), customer = new { name = "Meena", phone = "9833333333" },
            lines = new[] { new { productId = product, qtyUnits = 4, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 40 } },
        }); // FEFO → NEAR

        var found = await c.Get("/api/v1/batches?q=NEAR");
        var near = found.EnumerateArray().Single(b => b.GetProperty("batchNo").GetString() == "NEAR");
        var nearId = near.GetProperty("batchId").GetGuid();
        Assert.Equal(16m, near.GetProperty("qtyOnHand").GetDecimal());

        var trace = await c.Get($"/api/v1/batches/{nearId}/trace");
        var sale = Assert.Single(trace.GetProperty("sales").EnumerateArray());
        Assert.Equal("9833333333", sale.GetProperty("customerPhone").GetString());
        Assert.Equal(20m, Assert.Single(trace.GetProperty("purchases").EnumerateArray()).GetProperty("qtyUnits").GetDecimal());

        await c.Post($"/api/v1/batches/{nearId}/block", new { reason = "Manufacturer recall notice" });
        var pos = await c.Get("/api/v1/pos/search?q=Counter");
        Assert.DoesNotContain(pos[0].GetProperty("batches").EnumerateArray(), b => b.GetProperty("batchNo").GetString() == "NEAR");
        Assert.Equal(30m, pos[0].GetProperty("availableUnits").GetDecimal());

        var s = await c.SellAsync(product, 10, 100); // must come from FAR
        Assert.Equal("FAR", s.GetProperty("lines")[0].GetProperty("batchNo").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "batch_blocked"),
            await c.Try(HttpMethod.Post, "/api/v1/sales", new
            {
                clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, batchId = nearId, qtyUnits = 1, discountPercent = 0 } },
                payments = new[] { new { mode = "Cash", amount = 10 } },
            }));

        await c.Post($"/api/v1/batches/{nearId}/unblock", new { });
        Assert.Equal("NEAR", (await c.SellAsync(product, 1, 10)).GetProperty("lines")[0].GetProperty("batchNo").GetString());
    }

    [Fact]
    public async Task Cashier_cannot_count_stock_block_batches_or_change_settings()
    {
        var (owner, _) = await StockedAsync();
        var cashier = await owner.AddUserAsync("Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Post, "/api/v1/stock-counts", new { title = "x" })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, "/api/v1/batches?q=NEAR")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Put, "/api/v1/settings", new { cashierMaxDiscountPercent = 100, requireOpenShift = false })).Status);
        // but can run their own drawer
        await cashier.Post("/api/v1/shifts/open", new { openingCash = 0 });
    }
}
