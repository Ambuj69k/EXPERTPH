using System.Net;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>General ledger generated from documents: balances, statements, control checks, locking.</summary>
[Collection("api")]
public class Phase6Tests(ApiFixture f)
{
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private static decimal Bal(JsonElement tb, string code) =>
        tb.GetProperty("rows").EnumerateArray().SingleOrDefault(r => r.GetProperty("code").GetString() == code) is { ValueKind: JsonValueKind.Object } r
            ? r.GetProperty("balance").GetDecimal() : 0m;

    private static async Task<Guid> Account(TenantClient c, string code) =>
        (await c.Get("/api/v1/accounting/accounts")).EnumerateArray().Single(a => a.GetProperty("code").GetString() == code).GetProperty("id").GetGuid();

    [Fact]
    public async Task Ledger_from_documents_balances_and_ties_to_subledgers()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Ledger Tab"); // 10/strip, MRP 100, rate 70, GST 12% → cost ₹7/tab
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("LG1", 12, 5, 100, 70)); // 350 + 42 = 392

        var s1 = await c.SellAsync(product, 15, 150); // taxable 133.93 + 16.07, cost 105
        var credit = await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), customer = new { name = "Udhaar", phone = "9600000001" },
            lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } }, payments = new[] { new { mode = "Credit", amount = 100 } },
        }); // 89.29 + 10.71, cost 70
        await c.Post("/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = s1.GetProperty("id").GetGuid(), reason = "excess", refundMode = "Cash",
            lines = new[] { new { salesInvoiceLineId = s1.GetProperty("lines")[0].GetProperty("id").GetGuid(), qtyUnits = 5 } },
        }); // 44.64 + 5.36, cost back 35
        await c.Post("/api/v1/customer-receipts", new { customerId = credit.GetProperty("customerId").GetGuid(), receiptDate = Today, mode = "Upi", amount = 40, reference = "U9" });
        await c.Post("/api/v1/supplier-payments", new { supplierId = supplier, paymentDate = Today, mode = "BankTransfer", amount = 200, reference = "NEFT1" });
        var s3 = await c.SellAsync(product, 5, 50);
        await c.Post($"/api/v1/sales/{s3.GetProperty("id").GetGuid()}/cancel", new { reason = "mistake" });
        await c.Post("/api/v1/accounting/journals", new
        {
            date = Today, narration = "Shop rent",
            lines = new object[] { new { accountId = await Account(c, "5600"), debit = 1000, credit = 0 }, new { accountId = await Account(c, "1010"), debit = 0, credit = 1000 } },
        });

        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        Assert.Equal(100m, Bal(tb, "1000"));      // cash: 150 − 50 refund (cancelled bill nets to zero)
        Assert.Equal(40m, Bal(tb, "1020"));       // UPI clearing
        Assert.Equal(-1200m, Bal(tb, "1010"));    // bank: −200 supplier − 1000 rent
        Assert.Equal(60m, Bal(tb, "1100"));       // receivables: 100 − 40
        Assert.Equal(210m, Bal(tb, "1200"));      // inventory: 30 tabs × ₹7
        Assert.Equal(42m, Bal(tb, "1300"));       // GST input
        Assert.Equal(192m, Bal(tb, "2000"));      // payables: 392 − 200
        Assert.Equal(21.42m, Bal(tb, "2100"));    // GST output: 16.07 + 10.71 − 5.36
        Assert.Equal(223.22m, Bal(tb, "4000"));   // sales
        Assert.Equal(-44.64m, Bal(tb, "4010"));   // sales returns (contra income)
        Assert.Equal(140m, Bal(tb, "5000"));      // COGS: 105 + 70 − 35

        var pl = await c.Get($"/api/v1/accounting/profit-and-loss?from={Today}&to={Today}");
        Assert.Equal(178.58m, pl.GetProperty("totalIncome").GetDecimal());
        Assert.Equal(1140m, pl.GetProperty("totalExpenses").GetDecimal());
        Assert.Equal(-961.42m, pl.GetProperty("netProfit").GetDecimal());
        var bs = await c.Get($"/api/v1/accounting/balance-sheet?asOf={Today}");
        Assert.True(bs.GetProperty("balanced").GetBoolean());
        Assert.Equal(-961.42m, bs.GetProperty("currentEarnings").GetDecimal());

        foreach (var check in (await c.Get("/api/v1/accounting/control-checks")).EnumerateArray())
            Assert.True(check.GetProperty("difference").GetDecimal() == 0, $"{check.GetProperty("name").GetString()} differs by {check.GetProperty("difference")}");

        var gst = await c.Get($"/api/v1/accounting/gst-summary?from={Today}&to={Today}");
        Assert.Equal(21.42m, gst.GetProperty("outputTax").GetDecimal());
        Assert.Equal(42m, gst.GetProperty("inputTax").GetDecimal());
        Assert.Equal(-20.58m, gst.GetProperty("netTax").GetDecimal());

        var cash = await c.Get($"/api/v1/accounting/cash-book?from={Today}&to={Today}");
        Assert.Equal(100m, cash.GetProperty("closing").GetDecimal());
        Assert.Equal(0, (await c.Post("/api/v1/accounting/sync", new { })).GetProperty("entriesCreated").GetInt32()); // idempotent

        var csv = await c.Http.GetStringAsync($"/api/v1/accounting/export.csv?from={Today}&to={Today}");
        Assert.StartsWith("Date,Voucher,Source,Reference,Account Code,Account,Debit,Credit,Narration", csv);
        Assert.Contains("Shop rent", csv);
    }

    [Fact]
    public async Task Supplier_return_claims_and_writeoff_post_correctly()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Claim Tab");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("CL1", 2, 5, 100, 70)); // AP 392
        var cand = await c.Get("/api/v1/purchase-returns/candidates?days=120");
        var ret = await c.Post("/api/v1/purchase-returns", new { supplierId = supplier, reason = "NearExpiry", lines = new[] { new { batchId = cand[0].GetProperty("batchId").GetGuid(), qtyUnits = 10 } } });
        var id = ret.GetProperty("id").GetGuid();
        await c.Post($"/api/v1/purchase-returns/{id}/dispatch", new { });

        var mid = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.Equal(78.40m, Bal(mid, "1150")); // claim incl. GST
        Assert.Equal(280m, Bal(mid, "1200"));   // 350 − 70
        Assert.Equal(33.60m, Bal(mid, "1300")); // 42 − 8.40

        await c.Post($"/api/v1/purchase-returns/{id}/settle", new { creditNoteNo = "CN1", creditNoteDate = Today, acceptedAmount = 60 });
        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.Equal(0m, Bal(tb, "1150"));
        Assert.Equal(332m, Bal(tb, "2000"));    // 392 − 60 credit note
        Assert.Equal(18.40m, Bal(tb, "5110"));  // written off
        foreach (var check in (await c.Get("/api/v1/accounting/control-checks")).EnumerateArray())
            Assert.Equal(0m, check.GetProperty("difference").GetDecimal());
    }

    [Fact]
    public async Task Manual_journal_rules_period_lock_and_accountant_role()
    {
        var c = await f.NewTenantAsync();
        var rent = await Account(c, "5600");
        var bank = await Account(c, "1010");
        var ar = await Account(c, "1100");
        var yesterday = DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/accounting/journals", new
        {
            date = Today, narration = "unbalanced", lines = new object[] { new { accountId = rent, debit = 10, credit = 0 }, new { accountId = bank, debit = 0, credit = 9 } },
        })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/accounting/journals", new
        {
            date = Today, narration = "touch receivables", lines = new object[] { new { accountId = ar, debit = 10, credit = 0 }, new { accountId = bank, debit = 0, credit = 10 } },
        })).Status);

        var j = await c.Post("/api/v1/accounting/journals", new
        {
            date = yesterday, narration = "Electricity", lines = new object[] { new { accountId = rent, debit = 500, credit = 0 }, new { accountId = bank, debit = 0, credit = 500 } },
        });
        await c.Post("/api/v1/accounting/close", new { upTo = yesterday });
        Assert.Equal((HttpStatusCode.Conflict, "period_locked"), await c.Try(HttpMethod.Post, "/api/v1/accounting/journals", new
        {
            date = yesterday, narration = "late", lines = new object[] { new { accountId = rent, debit = 1, credit = 0 }, new { accountId = bank, debit = 0, credit = 1 } },
        }));
        var rev = await c.Post($"/api/v1/accounting/journals/{j.GetProperty("id").GetGuid()}/reverse", new { });
        Assert.Equal(Today, rev.GetProperty("date").GetString()); // reversal lands in the open period
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, $"/api/v1/accounting/journals/{j.GetProperty("id").GetGuid()}/reverse")).Status);
        Assert.Equal(0m, Bal(await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}"), "5600"));

        var accountant = await c.AddUserAsync("Accountant");
        Assert.True((await accountant.Get($"/api/v1/accounting/trial-balance?asOf={Today}")).GetProperty("balanced").GetBoolean());
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Try(HttpMethod.Get, "/api/v1/pos/search?q=a")).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await accountant.Try(HttpMethod.Post, "/api/v1/accounting/close", new { upTo = yesterday })).Status);
        var cashier = await c.AddUserAsync("Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, $"/api/v1/accounting/trial-balance?asOf={Today}")).Status);
    }
}
