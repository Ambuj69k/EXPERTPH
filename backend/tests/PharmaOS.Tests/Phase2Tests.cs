using System.Net;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Returns to supplier, payables and receivables.</summary>
[Collection("api")]
public class Phase2Tests(ApiFixture f)
{
    // Product: strip of 10, MRP 100, purchase rate 70 ex-GST, GST 12% → cost 7.00/tab.
    private async Task<(TenantClient C, Guid Product, Guid Supplier, JsonElement Purchase)> SetupAsync()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Return Me");
        var supplier = await c.CreateSupplierAsync();
        // NEAR: 2 strips expiring next month; FAR: 3 strips in a year → net 5×70 = 350 + 42 GST = 392
        var purchase = await c.PurchaseAsync(supplier, product, ("NEAR", 1, 2, 100, 70), ("FAR", 12, 3, 100, 70));
        return (c, product, supplier, purchase);
    }

    private static Guid BatchId(JsonElement candidates, string batchNo) =>
        candidates.EnumerateArray().Single(x => x.GetProperty("batchNo").GetString() == batchNo).GetProperty("batchId").GetGuid();

    [Fact]
    public async Task Near_expiry_return_dispatch_settle_and_recovery()
    {
        var (c, product, supplier, _) = await SetupAsync();

        var candidates = await c.Get("/api/v1/purchase-returns/candidates?days=60");
        var near = Assert.Single(candidates.EnumerateArray());
        Assert.Equal("NEAR", near.GetProperty("batchNo").GetString());
        Assert.Equal(supplier, near.GetProperty("suggestedSupplierId").GetGuid());
        Assert.Equal(156.80m, near.GetProperty("claimValue").GetDecimal()); // 20 × 7.00 = 140 + 12% = 156.80

        var draft = await c.Post("/api/v1/purchase-returns", new
        {
            supplierId = supplier, reason = "NearExpiry", lines = new[] { new { batchId = BatchId(candidates, "NEAR"), qtyUnits = 20 } },
        });
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.StartsWith("DMAIN", draft.GetProperty("returnNo").GetString());
        Assert.Equal(20m, (await c.Get("/api/v1/purchase-returns/candidates?days=60"))[0].GetProperty("qtyInOpenReturns").GetDecimal());
        Assert.Equal(50m, await c.StockUnitsAsync(product)); // draft does not move stock

        var id = draft.GetProperty("id").GetGuid();
        await c.Post($"/api/v1/purchase-returns/{id}/dispatch", new { });
        Assert.Equal(30m, await c.StockUnitsAsync(product));
        var pending = await c.Get("/api/v1/reports/return-recovery");
        Assert.Equal(156.80m, pending.GetProperty("pendingClaims").GetDecimal());

        var settled = await c.Post($"/api/v1/purchase-returns/{id}/settle", new
        {
            creditNoteNo = "cn-77", creditNoteDate = DateTime.Today.ToString("yyyy-MM-dd"), acceptedAmount = 120,
        });
        Assert.Equal("Settled", settled.GetProperty("status").GetString());
        Assert.Equal(36.80m, settled.GetProperty("writtenOffAmount").GetDecimal());
        Assert.Equal("CN-77", settled.GetProperty("supplierCreditNoteNo").GetString());

        var rec = await c.Get("/api/v1/reports/return-recovery");
        Assert.Equal(0m, rec.GetProperty("pendingClaims").GetDecimal());
        Assert.Equal(120m, rec.GetProperty("accepted").GetDecimal());
        Assert.Equal(76.53m, rec.GetProperty("recoveryPercent").GetDecimal()); // 120 / 156.80

        // credit note reduces what we owe the supplier: 392 − 120
        var payables = await c.Get("/api/v1/payables");
        Assert.Equal(272m, payables[0].GetProperty("balance").GetDecimal());
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Rejected_return_brings_stock_back_and_cannot_then_settle()
    {
        var (c, product, supplier, _) = await SetupAsync();
        var candidates = await c.Get("/api/v1/purchase-returns/candidates?days=400");
        var draft = await c.Post("/api/v1/purchase-returns", new
        {
            supplierId = supplier, reason = "Damaged", lines = new[] { new { batchId = BatchId(candidates, "FAR"), qtyUnits = 5 } },
        });
        var id = draft.GetProperty("id").GetGuid();
        await c.Post($"/api/v1/purchase-returns/{id}/dispatch", new { });
        Assert.Equal(45m, await c.StockUnitsAsync(product));

        var rejected = await c.Post($"/api/v1/purchase-returns/{id}/reject", new { reason = "Not damaged in transit per supplier" });
        Assert.Equal("Rejected", rejected.GetProperty("status").GetString());
        Assert.Equal(50m, await c.StockUnitsAsync(product));
        var settle = await c.Try(HttpMethod.Post, $"/api/v1/purchase-returns/{id}/settle", new
        {
            creditNoteNo = "X", creditNoteDate = DateTime.Today.ToString("yyyy-MM-dd"), acceptedAmount = 1,
        });
        Assert.Equal(HttpStatusCode.Conflict, settle.Status);
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }

    [Fact]
    public async Task Cannot_return_more_than_on_hand_or_over_settle()
    {
        var (c, _, supplier, _) = await SetupAsync();
        var candidates = await c.Get("/api/v1/purchase-returns/candidates?days=60");
        var tooMany = await c.Try(HttpMethod.Post, "/api/v1/purchase-returns", new
        {
            supplierId = supplier, reason = "Expired", lines = new[] { new { batchId = BatchId(candidates, "NEAR"), qtyUnits = 21 } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.Status);

        var draft = await c.Post("/api/v1/purchase-returns", new
        {
            supplierId = supplier, reason = "Expired", lines = new[] { new { batchId = BatchId(candidates, "NEAR"), qtyUnits = 10 } },
        });
        var id = draft.GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, $"/api/v1/purchase-returns/{id}/settle", new
        {
            creditNoteNo = "EARLY", creditNoteDate = DateTime.Today.ToString("yyyy-MM-dd"), acceptedAmount = 1,
        })).Status); // must dispatch first
        await c.Post($"/api/v1/purchase-returns/{id}/dispatch", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/purchase-returns/{id}/settle", new
        {
            creditNoteNo = "OVER", creditNoteDate = DateTime.Today.ToString("yyyy-MM-dd"), acceptedAmount = 1000,
        })).Status);
    }

    [Fact]
    public async Task Supplier_payment_allocates_oldest_first_and_protects_purchases()
    {
        var (c, product, supplier, first) = await SetupAsync();
        var second = await c.PurchaseAsync(supplier, product, ("B2", 10, 1, 100, 70)); // 70 + 8.40 = 78.40 → 78

        var noRef = await c.Try(HttpMethod.Post, "/api/v1/supplier-payments", new
        {
            supplierId = supplier, paymentDate = DateTime.Today.ToString("yyyy-MM-dd"), mode = "BankTransfer", amount = 100,
        });
        Assert.Equal(HttpStatusCode.BadRequest, noRef.Status);

        var pay = await c.Post("/api/v1/supplier-payments", new
        {
            supplierId = supplier, paymentDate = DateTime.Today.ToString("yyyy-MM-dd"), mode = "Upi", amount = 420, reference = "UTR-1",
        });
        Assert.Equal(420m, pay.GetProperty("allocated").GetDecimal()); // 392 to first + 28 to second

        var st = await c.Get($"/api/v1/payables/suppliers/{supplier}");
        Assert.Equal(50m, st.GetProperty("balance").GetDecimal()); // 392 + 78 − 420
        var open = Assert.Single(st.GetProperty("openDocuments").EnumerateArray());
        Assert.Equal(second.GetProperty("supplierInvoiceNo").GetString(), open.GetProperty("documentNo").GetString());
        Assert.Equal(50m, open.GetProperty("outstanding").GetDecimal());

        var firstId = first.GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Conflict, "has_payments"),
            await c.Try(HttpMethod.Post, $"/api/v1/purchases/{firstId}/cancel", new { reason = "wrong" }));

        await c.Post($"/api/v1/supplier-payments/{pay.GetProperty("id").GetGuid()}/cancel", new { reason = "UTR bounced" });
        Assert.Equal(470m, (await c.Get($"/api/v1/payables/suppliers/{supplier}")).GetProperty("balance").GetDecimal());
        await c.Post($"/api/v1/purchases/{firstId}/cancel", new { reason = "wrong supplier" }); // now allowed
        Assert.Equal(78m, (await c.Get($"/api/v1/payables/suppliers/{supplier}")).GetProperty("balance").GetDecimal());

        var aging = await c.Get("/api/v1/payables/aging");
        Assert.Equal(78m, aging.GetProperty("netOutstanding").GetDecimal());
    }

    private static object CreditSale(Guid product, decimal units, decimal amount, string phone = "9811111111") => new
    {
        clientRequestId = Guid.NewGuid(), customer = new { name = "Ravi", phone },
        lines = new[] { new { productId = product, qtyUnits = units, discountPercent = 0 } },
        payments = new[] { new { mode = "Credit", amount } },
    };

    [Fact]
    public async Task Customer_credit_receipts_limit_and_credit_note_refund()
    {
        var (c, product, _, _) = await SetupAsync();
        var s1 = await c.Post("/api/v1/sales", CreditSale(product, 10, 100));
        var s2 = await c.Post("/api/v1/sales", CreditSale(product, 5, 50));
        var customer = s1.GetProperty("customerId").GetGuid();
        Assert.Equal(customer, s2.GetProperty("customerId").GetGuid()); // same phone → same customer

        var receipt = await c.Post("/api/v1/customer-receipts", new
        {
            customerId = customer, receiptDate = DateTime.Today.ToString("yyyy-MM-dd"), mode = "Cash", amount = 120,
        });
        Assert.Equal(120m, receipt.GetProperty("allocated").GetDecimal()); // 100 to s1, 20 to s2
        var st = await c.Get($"/api/v1/receivables/customers/{customer}");
        Assert.Equal(30m, st.GetProperty("balance").GetDecimal());
        Assert.Equal(30m, Assert.Single(st.GetProperty("openDocuments").EnumerateArray()).GetProperty("outstanding").GetDecimal());

        Assert.Equal((HttpStatusCode.Conflict, "has_receipts"),
            await c.Try(HttpMethod.Post, $"/api/v1/sales/{s1.GetProperty("id").GetGuid()}/cancel", new { reason = "x" }));

        await c.Put($"/api/v1/customers/{customer}", new { name = "Ravi", phone = "9811111111", creditLimit = 50 });
        Assert.Equal((HttpStatusCode.Conflict, "credit_limit"), await c.Try(HttpMethod.Post, "/api/v1/sales", CreditSale(product, 3, 30)));
        await c.Post("/api/v1/sales", CreditSale(product, 2, 20)); // 30 + 20 = 50 → exactly at limit
        Assert.Equal(50m, (await c.Get("/api/v1/receivables"))[0].GetProperty("balance").GetDecimal());

        // returning all of s2 as a credit note wipes what was still owed on it
        await c.Post("/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = s2.GetProperty("id").GetGuid(), reason = "Not needed", refundMode = "Credit",
            lines = new[] { new { salesInvoiceLineId = s2.GetProperty("lines")[0].GetProperty("id").GetGuid(), qtyUnits = 5 } },
        });
        Assert.Equal(0m, (await c.Get($"/api/v1/receivables/customers/{customer}")).GetProperty("balance").GetDecimal());
    }

    [Fact]
    public async Task Cashier_can_collect_but_not_pay_suppliers_or_change_limits()
    {
        var (owner, product, supplier, _) = await SetupAsync();
        var sale = await owner.Post("/api/v1/sales", CreditSale(product, 10, 100, "9822222222"));
        var cashier = await owner.AddUserAsync("Cashier");
        var customer = sale.GetProperty("customerId").GetGuid();

        await cashier.Post("/api/v1/customer-receipts", new
        {
            customerId = customer, receiptDate = DateTime.Today.ToString("yyyy-MM-dd"), mode = "Upi", amount = 40, reference = "U1",
        });
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Post, "/api/v1/supplier-payments", new
        {
            supplierId = supplier, paymentDate = DateTime.Today.ToString("yyyy-MM-dd"), mode = "Cash", amount = 10,
        })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Put, $"/api/v1/customers/{customer}",
            new { name = "Ravi", phone = "9822222222", creditLimit = 100000 })).Status);
    }
}
