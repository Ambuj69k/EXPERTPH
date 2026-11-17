using System.Net;
using System.Text.Json;

namespace PharmaOS.Tests;

[Collection("api")]
public class RetailFlowTests(ApiFixture f)
{
    private const decimal Mrp = 100m;   // per strip of 10 → ₹10/tab
    private const decimal Rate = 70m;   // purchase rate per strip, ex-GST

    private async Task<(TenantClient C, Guid Product, Guid Supplier)> StockedAsync(decimal latePacks = 5, decimal earlyPacks = 1)
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Paracip 500");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("LATE", 12, latePacks, Mrp, Rate), ("EARLY", 3, earlyPacks, Mrp, Rate));
        return (c, product, supplier);
    }

    [Fact]
    public async Task Sale_allocates_FEFO_and_splits_GST_from_MRP()
    {
        var (c, product, _) = await StockedAsync();
        Assert.Equal(60m, await c.StockUnitsAsync(product));

        var sale = await c.SellAsync(product, 15, 150);

        var lines = sale.GetProperty("lines").EnumerateArray().ToList();
        Assert.Equal(2, lines.Count);
        Assert.Equal("EARLY", lines[0].GetProperty("batchNo").GetString());   // earliest expiry first
        Assert.Equal(10m, lines[0].GetProperty("qtyUnits").GetDecimal());
        Assert.Equal("LATE", lines[1].GetProperty("batchNo").GetString());
        Assert.Equal(5m, lines[1].GetProperty("qtyUnits").GetDecimal());
        Assert.Equal(150m, sale.GetProperty("netAmount").GetDecimal());
        Assert.Equal(133.93m, sale.GetProperty("taxableAmount").GetDecimal()); // 89.29 + 44.64
        Assert.Equal(16.07m, sale.GetProperty("taxAmount").GetDecimal());
        Assert.Matches(@"^SMAIN\d{4}-000001$", sale.GetProperty("invoiceNo").GetString());
        Assert.Equal(45m, await c.StockUnitsAsync(product));
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Retrying_same_submit_does_not_double_bill()
    {
        var (c, product, _) = await StockedAsync();
        var key = Guid.NewGuid();
        var first = await c.SellAsync(product, 10, 100, key);
        var second = await c.SellAsync(product, 10, 100, key);
        Assert.Equal(first.GetProperty("invoiceNo").GetString(), second.GetProperty("invoiceNo").GetString());
        Assert.Equal(50m, await c.StockUnitsAsync(product));
    }

    [Fact]
    public async Task Cannot_oversell_or_underpay()
    {
        var (c, product, _) = await StockedAsync();
        var over = await c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 61, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 610 } },
        });
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_stock"), over);

        var underpaid = await c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 99 } },
        });
        Assert.Equal((HttpStatusCode.BadRequest, "validation"), underpaid);
        Assert.Equal(60m, await c.StockUnitsAsync(product)); // failed bills leave no trace in stock
    }

    [Fact]
    public async Task Split_payment_and_credit_requires_customer()
    {
        var (c, product, _) = await StockedAsync();
        var noCustomer = await c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new object[] { new { mode = "Upi", amount = 60, reference = "UTR123" }, new { mode = "Credit", amount = 40 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, noCustomer.Status);

        var sale = await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), customer = new { name = "Ramesh", phone = "+91 98765 43210" },
            lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new object[] { new { mode = "Upi", amount = 60, reference = "UTR123" }, new { mode = "Credit", amount = 40 } },
        });
        Assert.Equal("9876543210", sale.GetProperty("customerPhone").GetString());
        Assert.Equal(2, sale.GetProperty("payments").GetArrayLength());
    }

    [Fact]
    public async Task Partial_return_restocks_and_cannot_exceed_sold_qty()
    {
        var (c, product, _) = await StockedAsync();
        var sale = await c.SellAsync(product, 15, 150);
        var lateLine = sale.GetProperty("lines")[1];
        var lineId = lateLine.GetProperty("id").GetGuid();

        var ret = await c.Post("/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = sale.GetProperty("id").GetGuid(), reason = "Doctor changed medicine",
            refundMode = "Cash", lines = new[] { new { salesInvoiceLineId = lineId, qtyUnits = 5 } },
        });
        Assert.Equal(50m, ret.GetProperty("refundAmount").GetDecimal());
        Assert.Equal(50m, await c.StockUnitsAsync(product));

        var again = await c.Try(HttpMethod.Post, "/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = sale.GetProperty("id").GetGuid(), reason = "x",
            refundMode = "Cash", lines = new[] { new { salesInvoiceLineId = lineId, qtyUnits = 1 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, again.Status);

        var report = await c.Get($"/api/v1/reports/daily-sales?date={sale.GetProperty("businessDate").GetString()}");
        Assert.Equal(150m, report.GetProperty("netSales").GetDecimal());
        Assert.Equal(50m, report.GetProperty("returns").GetDecimal());
        Assert.Equal(100m, report.GetProperty("netAfterReturns").GetDecimal());
        // cost: 10 units sold at 7.00/unit net after return → 70; taxable 133.93 - 44.64 = 89.29; margin 19.29
        Assert.Equal(70m, report.GetProperty("costOfGoods").GetDecimal());
        Assert.Equal(19.29m, report.GetProperty("grossMargin").GetDecimal());
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Cancel_sale_restores_stock_and_blocks_double_cancel()
    {
        var (c, product, _) = await StockedAsync();
        var sale = await c.SellAsync(product, 10, 100);
        var id = sale.GetProperty("id").GetGuid();
        var cancelled = await c.Post($"/api/v1/sales/{id}/cancel", new { reason = "Wrong customer" });
        Assert.Equal("Cancelled", cancelled.GetProperty("status").GetString());
        Assert.Equal(60m, await c.StockUnitsAsync(product));
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, $"/api/v1/sales/{id}/cancel", new { reason = "again" })).Status);
    }

    [Fact]
    public async Task Tenants_cannot_see_or_use_each_others_data()
    {
        var (a, product, _) = await StockedAsync();
        var b = await f.NewTenantAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await b.Try(HttpMethod.Get, $"/api/v1/products/{product}")).Status);
        Assert.Equal(0, (await b.Get("/api/v1/products")).GetProperty("total").GetInt32());
        var steal = await b.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 100 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, steal.Status);
        Assert.Equal(60m, await a.StockUnitsAsync(product));
    }

    [Fact]
    public async Task Cashier_permissions_and_discount_limit()
    {
        var (owner, product, supplier) = await StockedAsync();
        var cashier = await owner.AddUserAsync("Cashier");

        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, "/api/v1/purchases")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, "/api/v1/reports/near-expiry")).Status);

        var bigDiscount = await cashier.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 15 } },
            payments = new[] { new { mode = "Cash", amount = 85 } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, bigDiscount.Status);

        var ok = await cashier.SellAsync(product, 10, 95, discount: 5);
        Assert.Equal(95m, ok.GetProperty("netAmount").GetDecimal());
    }

    [Fact]
    public async Task Purchase_rules_duplicate_expired_and_cancel_after_sale()
    {
        var (c, product, supplier) = await StockedAsync();

        // duplicate supplier invoice number
        var body = new
        {
            supplierId = supplier, supplierInvoiceNo = "DUP-1", invoiceDate = DateTime.Today.ToString("yyyy-MM-dd"), otherCharges = 0,
            lines = new[] { new { productId = product, batchNo = "B9", expiryDate = TenantClient.Expiry(6), mrp = Mrp, rate = Rate, qtyPacks = 1, freeQtyPacks = 1, discountPercent = 0 } },
        };
        var draft = await c.Post("/api/v1/purchases", body);
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_invoice"), await c.Try(HttpMethod.Post, "/api/v1/purchases", body));

        // free goods are received into stock
        var posted = await c.Post($"/api/v1/purchases/{draft.GetProperty("id").GetGuid()}/post", new { });
        Assert.Equal(80m, await c.StockUnitsAsync(product)); // 60 + (1 paid + 1 free) * 10
        Assert.Equal(78m, posted.GetProperty("netAmount").GetDecimal()); // 70 + 12% GST = 78.40 → rounded 78

        // expired batch is rejected
        var expired = await c.Try(HttpMethod.Post, "/api/v1/purchases", body with { supplierInvoiceNo = "EXP-1",
            lines = new[] { new { productId = product, batchNo = "OLD", expiryDate = TenantClient.Expiry(-2), mrp = Mrp, rate = Rate, qtyPacks = 1, freeQtyPacks = 0, discountPercent = 0 } } });
        Assert.Equal(HttpStatusCode.BadRequest, expired.Status);

        // a purchase whose stock was sold cannot simply be cancelled
        await c.SellAsync(product, 80, 800);
        var cancel = await c.Try(HttpMethod.Post, $"/api/v1/purchases/{draft.GetProperty("id").GetGuid()}/cancel", new { reason = "test" });
        Assert.Equal((HttpStatusCode.Conflict, "stock_consumed"), cancel);
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Draft_purchase_can_be_edited_before_posting()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Edit Me");
        var supplier = await c.CreateSupplierAsync();
        object Body(int packs, int? version) => new
        {
            supplierId = supplier, supplierInvoiceNo = "ED-1", invoiceDate = DateTime.Today.ToString("yyyy-MM-dd"), otherCharges = 10, version,
            lines = new[] { new { productId = product, batchNo = "E1", expiryDate = TenantClient.Expiry(8), mrp = Mrp, rate = Rate, qtyPacks = packs, freeQtyPacks = 0, discountPercent = 10 } },
        };
        var draft = await c.Post("/api/v1/purchases", Body(1, null));
        var id = draft.GetProperty("id").GetGuid();
        var edited = await c.Put($"/api/v1/purchases/{id}", Body(3, draft.GetProperty("version").GetInt32()));
        // 3 × 70 = 210, −10% = 189, +12% GST = 211.68, +10 other = 221.68 → 222
        Assert.Equal(222m, edited.GetProperty("netAmount").GetDecimal());
        Assert.Single(edited.GetProperty("lines").EnumerateArray());
        await c.Post($"/api/v1/purchases/{id}/post", new { });
        Assert.Equal(30m, await c.StockUnitsAsync(product));
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Put, $"/api/v1/purchases/{id}", Body(1, edited.GetProperty("version").GetInt32()))).Status);
    }

    [Fact]
    public async Task Stale_product_update_is_rejected()
    {
        var c = await f.NewTenantAsync();
        var id = await c.CreateProductAsync("Azee 500", packSize: 3);
        object Update(int version, string name) => new
        {
            name, composition = "Azithromycin", manufacturer = "Acme", hsnCode = "3004", gstRate = 12, packSize = 3, packUnit = "Strip",
            baseUnit = "Tab", allowLooseSale = true, schedule = "H", reorderLevelUnits = 0, isActive = true, version,
        };
        var v1 = await c.Put($"/api/v1/products/{id}", Update(0, "Azee 500 mg"));
        Assert.Equal(1, v1.GetProperty("version").GetInt32());
        Assert.Equal((HttpStatusCode.Conflict, "concurrency"), await c.Try(HttpMethod.Put, $"/api/v1/products/{id}", Update(0, "Stale edit")));
    }

    [Fact]
    public async Task Pack_only_and_scheduled_drug_rules()
    {
        var c = await f.NewTenantAsync();
        var supplier = await c.CreateSupplierAsync();
        var syrup = await c.CreateProductAsync("Cough Syrup", packSize: 1, loose: false);
        var h1 = await c.CreateProductAsync("Restricted Tab", packSize: 10, schedule: "H1");
        await c.PurchaseAsync(supplier, syrup, ("S1", 12, 10, 120, 80));
        await c.PurchaseAsync(supplier, h1, ("H1A", 12, 2, 200, 120));

        var tenTabs = await c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = h1, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 200 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tenTabs.Status); // needs patient + prescriber

        var ok = await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), customer = new { name = "Sita", phone = "9000000001" }, doctorName = "Dr. Rao",
            lines = new[] { new { productId = h1, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 200 } },
        });
        Assert.Equal("Dr. Rao", ok.GetProperty("doctorName").GetString());
    }

    [Fact]
    public async Task Concurrent_bills_for_last_units_never_oversell()
    {
        var c = await f.NewTenantAsync();
        var supplier = await c.CreateSupplierAsync();
        var product = await c.CreateProductAsync("Last Stock");
        await c.PurchaseAsync(supplier, product, ("ONLY", 12, 1, Mrp, Rate)); // 10 units

        var attempts = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 100 } },
        })));

        Assert.Equal(1, attempts.Count(a => a.Status == HttpStatusCode.OK));
        Assert.All(attempts.Where(a => a.Status != HttpStatusCode.OK), a => Assert.Equal(HttpStatusCode.Conflict, a.Status));
        Assert.Equal(0m, await c.StockUnitsAsync(product));
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Opening_stock_and_near_expiry_report()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Old Cream", packSize: 1, gst: 18);
        var adj = await c.Post("/api/v1/stock/adjustments", new
        {
            isOpening = true, reason = "Migration from old software",
            lines = new[] { new { productId = product, batchNo = "OC1", expiryDate = TenantClient.Expiry(1), mrp = 50, purchaseRate = 30, qtyUnits = 4 } },
        });
        Assert.StartsWith("AMAIN", adj.GetProperty("adjustmentNo").GetString());

        var near = await c.Get("/api/v1/reports/near-expiry?days=90");
        var row = Assert.Single(near.EnumerateArray());
        Assert.Equal(4m, row.GetProperty("qtyUnits").GetDecimal());
        Assert.Equal(120m, row.GetProperty("valueAtCost").GetDecimal());

        var negative = await c.Try(HttpMethod.Post, "/api/v1/stock/adjustments", new
        {
            isOpening = false, reason = "Breakage",
            lines = new[] { new { productId = product, batchId = row.GetProperty("batchId").GetGuid(), qtyUnits = -5 } },
        });
        Assert.Equal(HttpStatusCode.Conflict, negative.Status); // only 4 on hand
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        var anon = f.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync("/api/v1/products")).StatusCode);
    }
}
