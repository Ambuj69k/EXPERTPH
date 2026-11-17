using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

[Collection("api")]
public class DeliveryChargeTests(ApiFixture f)
{
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private static decimal Bal(JsonElement tb, string code) =>
        tb.GetProperty("rows").EnumerateArray().SingleOrDefault(r => r.GetProperty("code").GetString() == code) is { ValueKind: JsonValueKind.Object } r
            ? r.GetProperty("balance").GetDecimal() : 0m;

    [Fact]
    public async Task Delivery_charge_on_bill_books_and_gst()
    {
        var c = await f.NewTenantAsync();
        var product = await c.CreateProductAsync("Deliver Tab"); // ₹10/tab, 12% GST
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("DL1", 12, 5, 100, 70));

        var sale = await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), deliveryCharge = 40,
            lines = new[] { new { productId = product, qtyUnits = 10, discountPercent = 0 } },
            payments = new[] { new { mode = "Cash", amount = 140 } },
        });
        Assert.Equal(140m, sale.GetProperty("netAmount").GetDecimal());
        Assert.Equal(33.90m, sale.GetProperty("deliveryChargeTaxable").GetDecimal());
        Assert.Equal(6.10m, sale.GetProperty("deliveryChargeTax").GetDecimal());
        Assert.Equal(16.81m, sale.GetProperty("taxAmount").GetDecimal()); // 10.71 on goods + 6.10 on delivery

        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        Assert.Equal(33.90m, Bal(tb, "4300"));
        Assert.Equal(16.81m, Bal(tb, "2100"));
        var gst = await c.Get($"/api/v1/accounting/gst-summary?from={Today}&to={Today}");
        var r18 = gst.GetProperty("outwardSupplies").EnumerateArray().Single(r => r.GetProperty("rate").GetDecimal() == 18m);
        Assert.Equal(6.10m, r18.GetProperty("tax").GetDecimal());
    }

    [Fact]
    public async Task Online_delivery_order_adds_charge_unless_above_free_threshold()
    {
        var c = await f.NewTenantAsync();
        var me = await c.Get("/api/v1/me");
        var branch = me.GetProperty("branchId").GetGuid();
        var product = (await c.Post("/api/v1/products", new
        {
            name = "Online Deliver", gstRate = 12, packSize = 10, packUnit = "Strip", baseUnit = "Tab", allowLooseSale = true, schedule = "None",
            reorderLevelUnits = 0, isActive = true, onlineListed = true,
        })).GetProperty("id").GetGuid();
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("OD1", 12, 20, 100, 70)); // 200 tabs @ ₹10

        var platform = f.Factory.CreateClient();
        var login = await (await platform.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json))
            .Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        platform.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.GetProperty("token").GetString());
        await TenantClient.EnsureOk(await platform.PutAsJsonAsync($"/api/v1/platform/tenants/{me.GetProperty("tenantId").GetGuid()}/subscription",
            new { planCode = "chain", status = "Active", extraModules = "online", migration = "Completed" }, ApiFixture.Json));
        await c.Put($"/api/v1/online/settings/{branch}", new { enabled = true, deliveryPincodes = "400001", deliveryCharge = 30, freeDeliveryAbove = 500, deliveryChargeGstRate = 18 });

        var anon = f.Factory.CreateClient();
        var store = await anon.GetFromJsonAsync<JsonElement>($"/api/v1/public/stores/{branch}", ApiFixture.Json);
        Assert.Equal(30m, store.GetProperty("deliveryCharge").GetDecimal());

        async Task<decimal> OrderAndPack(int units, string phone)
        {
            await TenantClient.EnsureOk(await anon.PostAsJsonAsync($"/api/v1/public/stores/{branch}/orders", new
            {
                name = "Del", phone, fulfilment = "Delivery", address = "1 Main Rd", pincode = "400001",
                lines = new[] { new { productId = product, qtyUnits = units } },
            }, ApiFixture.Json));
            var id = (await c.Get("/api/v1/online/orders?status=Submitted"))[0].GetProperty("id").GetGuid();
            await c.Post($"/api/v1/online/orders/{id}/review", new { accept = true, lines = new[] { new { productId = product, approvedQtyUnits = units } } });
            return (await c.Post($"/api/v1/online/orders/{id}/pack", new { })).GetProperty("amountDue").GetDecimal();
        }
        Assert.Equal(130m, await OrderAndPack(10, "9444400001"));   // ₹100 + ₹30 delivery
        Assert.Equal(600m, await OrderAndPack(60, "9444400002"));   // ₹600 ≥ ₹500 → free delivery
    }
}
