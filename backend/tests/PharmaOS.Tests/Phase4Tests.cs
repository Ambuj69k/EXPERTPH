using System.Net;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Loyalty, consent-gated reminders, customer merge and retention report.</summary>
[Collection("api")]
public class Phase4Tests(ApiFixture f)
{
    private async Task<(TenantClient C, Guid Product)> StockedAsync()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Loyal Tab");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("L1", 12, 20, 100, 70)); // 200 tabs at ₹10
        return (c, product);
    }

    private static object Sale(Guid product, decimal units, string? phone, params (string Mode, decimal Amount)[] pays) => new
    {
        clientRequestId = Guid.NewGuid(),
        customer = phone is null ? null : new { name = "Cust " + phone[^4..], phone },
        lines = new[] { new { productId = product, qtyUnits = units, discountPercent = 0 } },
        payments = pays.Select(p => new { mode = p.Mode, amount = p.Amount }).ToArray(),
    };

    private static async Task<int> Points(TenantClient c, Guid customer) =>
        (await c.Get($"/api/v1/customers/{customer}/loyalty")).GetProperty("points").GetInt32();

    [Fact]
    public async Task Loyalty_earn_redeem_and_reversals()
    {
        var (c, product) = await StockedAsync();
        await c.Put("/api/v1/loyalty/settings", new { enabled = true, rupeesPerPoint = 100, pointValue = 1, minRedeemPoints = 0 });

        var s1 = await c.Post("/api/v1/sales", Sale(product, 35, "9700000001", ("Cash", 350)));
        var cust = s1.GetProperty("customerId").GetGuid();
        Assert.Equal(3, await Points(c, cust)); // floor(350 / 100)

        var s2 = await c.Post("/api/v1/sales", Sale(product, 10, "9700000001", ("Loyalty", 2), ("Cash", 98)));
        Assert.Equal(1, await Points(c, cust)); // 3 − 2 redeemed + floor(98/100)=0

        Assert.Equal((HttpStatusCode.Conflict, "insufficient_points"), await c.Try(HttpMethod.Post, "/api/v1/sales", Sale(product, 1, "9700000001", ("Loyalty", 5), ("Cash", 5))));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/sales", Sale(product, 1, null, ("Loyalty", 1), ("Cash", 9)))).Status);

        await c.Post($"/api/v1/sales/{s2.GetProperty("id").GetGuid()}/cancel", new { reason = "test" });
        Assert.Equal(3, await Points(c, cust)); // redeemed points given back

        await c.Post("/api/v1/sales-returns", new
        {
            clientRequestId = Guid.NewGuid(), salesInvoiceId = s1.GetProperty("id").GetGuid(), reason = "excess", refundMode = "Cash",
            lines = new[] { new { salesInvoiceLineId = s1.GetProperty("lines")[0].GetProperty("id").GetGuid(), qtyUnits = 5 } },
        });
        Assert.Equal(2, await Points(c, cust)); // ceil(3 × 50/350) = 1 taken back

        var lookup = await c.Get("/api/v1/customers/lookup?phone=9700000001");
        Assert.Equal(2, lookup.GetProperty("loyaltyPoints").GetInt32());
        Assert.Equal(2m, lookup.GetProperty("loyaltyValue").GetDecimal());
    }

    [Fact]
    public async Task Reminders_need_request_and_consent_and_follow_purchases()
    {
        var (c, product) = await StockedAsync();
        var s = await c.Post("/api/v1/sales", Sale(product, 10, "9700000002", ("Cash", 100)));
        var cust = s.GetProperty("customerId").GetGuid();
        var today = DateOnly.FromDateTime(DateTime.Today);

        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/reminders",
            new { customerId = cust, productId = product, intervalDays = 30, customerRequested = false })).Status);
        var rem = await c.Post("/api/v1/reminders", new
        {
            customerId = cust, productId = product, intervalDays = 30, nextDueDate = today.AddDays(5).ToString("yyyy-MM-dd"), customerRequested = true,
        });
        var rid = rem.GetProperty("id").GetGuid();
        Assert.False(rem.GetProperty("whatsAppConsent").GetBoolean());

        var skipped = await c.Post($"/api/v1/reminders/{rid}/notify", new { channel = "WhatsApp" });
        Assert.Equal("Skipped", skipped.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, skipped.GetProperty("clickToChatUrl").ValueKind);

        await c.Post($"/api/v1/customers/{cust}/consents", new { channel = "WhatsApp", purpose = "Reminders", granted = true, source = "verbal at counter" });
        await c.Put("/api/v1/notification-templates", new { key = "refill_reminder", channel = "WhatsApp", body = "Hi {{customerName}}, {{productName}} due {{dueDate}} - {{businessName}}", isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Put, "/api/v1/notification-templates",
            new { key = "refill_reminder", channel = "WhatsApp", body = "Hi {{diagnosis}}", isActive = true })).Status);

        var ready = await c.Post($"/api/v1/reminders/{rid}/notify", new { channel = "WhatsApp" });
        Assert.Equal("Pending", ready.GetProperty("status").GetString());
        Assert.StartsWith("Hi Cust 0002, Loyal Tab due", ready.GetProperty("message").GetString());
        Assert.StartsWith("https://wa.me/919700000002?text=", ready.GetProperty("clickToChatUrl").GetString());
        var sent = await c.Post($"/api/v1/notifications/{ready.GetProperty("logId").GetGuid()}/sent", new { });
        Assert.Equal("Sent", sent.GetProperty("status").GetString());

        await c.Post($"/api/v1/customers/{cust}/consents", new { channel = "WhatsApp", purpose = "Reminders", granted = false, source = "replied STOP" });
        Assert.Equal("Skipped", (await c.Post($"/api/v1/reminders/{rid}/notify", new { channel = "WhatsApp" })).GetProperty("status").GetString());

        var log = await c.Get($"/api/v1/notifications?customerId={cust}");
        Assert.Equal(3, log.GetProperty("total").GetInt32()); // skipped, sent, skipped — every attempt is recorded

        await c.Post("/api/v1/sales", Sale(product, 10, "9700000002", ("Cash", 100))); // buying again resets the due date
        var after = (await c.Get($"/api/v1/reminders?customerId={cust}"))[0];
        Assert.Equal(today.AddDays(30).ToString("yyyy-MM-dd"), after.GetProperty("nextDueDate").GetString());
        Assert.NotEqual(JsonValueKind.Null, after.GetProperty("lastContactedAt").ValueKind);
    }

    [Fact]
    public async Task Merge_moves_bills_dues_points_and_phone()
    {
        var (c, product) = await StockedAsync();
        await c.Put("/api/v1/loyalty/settings", new { enabled = true, rupeesPerPoint = 50, pointValue = 1, minRedeemPoints = 0 });
        var dup = await c.Post("/api/v1/sales", Sale(product, 10, "9700000003", ("Credit", 100)));
        var dupId = dup.GetProperty("customerId").GetGuid();
        var keep = await c.Post("/api/v1/customers", new { name = "Sunita Sharma", phone = (string?)null });
        var keepId = keep.GetProperty("id").GetGuid();

        var merged = await c.Post("/api/v1/customers/merge", new { sourceCustomerId = dupId, targetCustomerId = keepId });
        Assert.Equal("9700000003", merged.GetProperty("phone").GetString());
        Assert.Equal(1, merged.GetProperty("billCount").GetInt32());
        Assert.Equal(100m, merged.GetProperty("balance").GetDecimal());
        Assert.Equal(2, merged.GetProperty("loyaltyPoints").GetInt32());
        Assert.Equal(0, await Points(c, dupId));

        var found = await c.Get("/api/v1/customers?q=Cust");
        Assert.Equal(0, found.GetArrayLength()); // duplicate hidden
        Assert.Equal(keepId, (await c.Get("/api/v1/customers/lookup?phone=9700000003")).GetProperty("id").GetGuid());
        // next bill with that number lands on the surviving record
        var next = await c.Post("/api/v1/sales", Sale(product, 1, "9700000003", ("Cash", 10)));
        Assert.Equal(keepId, next.GetProperty("customerId").GetGuid());
    }

    [Fact]
    public async Task Retention_report_counts()
    {
        var (c, product) = await StockedAsync();
        await c.Post("/api/v1/sales", Sale(product, 1, "9700000011", ("Cash", 10)));
        await c.Post("/api/v1/sales", Sale(product, 2, "9700000011", ("Cash", 20)));
        await c.Post("/api/v1/sales", Sale(product, 3, "9700000012", ("Cash", 30)));
        await c.SellAsync(product, 1, 10); // walk-in
        var d = DateTime.Today.ToString("yyyy-MM-dd");
        var r = await c.Get($"/api/v1/reports/customer-retention?from={d}&to={d}");
        Assert.Equal(4, r.GetProperty("totalBills").GetInt32());
        Assert.Equal(3, r.GetProperty("identifiedBills").GetInt32());
        Assert.Equal(75m, r.GetProperty("identifiedSharePercent").GetDecimal());
        Assert.Equal(2, r.GetProperty("uniqueCustomers").GetInt32());
        Assert.Equal(2, r.GetProperty("newCustomers").GetInt32());
        Assert.Equal(1, r.GetProperty("repeatWithinPeriod").GetInt32());
        Assert.Equal(30m, r.GetProperty("topCustomers")[0].GetProperty("spend").GetDecimal());
    }
}
