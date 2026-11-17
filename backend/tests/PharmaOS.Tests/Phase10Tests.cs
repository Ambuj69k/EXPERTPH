using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Online orders: gated module, OTC-only public listing, pharmacist review, Rx upload, billing at packing, delivery handover.</summary>
[Collection("api")]
public class Phase10Tests(ApiFixture f)
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private async Task<TenantClient> PlatformAsync()
    {
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new TenantClient(http, f);
    }

    private static async Task<Guid> Product(TenantClient c, string name, decimal pack, decimal gst, string schedule) =>
        (await c.Post("/api/v1/products", new
        {
            name, composition = name, manufacturer = "Acme", hsnCode = "3004", gstRate = gst, packSize = pack, packUnit = "Strip", baseUnit = "Tab",
            allowLooseSale = true, schedule, reorderLevelUnits = 0, isActive = true, onlineListed = true,
        })).GetProperty("id").GetGuid();

    private static async Task<(HttpStatusCode, JsonElement)> Public(HttpClient http, HttpMethod m, string url, object? body = null)
    {
        var req = new HttpRequestMessage(m, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: ApiFixture.Json);
        var res = await http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        return (res.StatusCode, text.Length > 0 && (text[0] == '{' || text[0] == '[') ? JsonDocument.Parse(text).RootElement : default);
    }

    [Fact]
    public async Task Online_order_review_pack_deliver_and_cancel()
    {
        var platform = await PlatformAsync();
        var c = await f.NewTenantAsync();
        var me = await c.Get("/api/v1/me");
        var branch = me.GetProperty("branchId").GetGuid();
        var tid = me.GetProperty("tenantId").GetGuid();
        var otc = await Product(c, "Crocin Online", 10, 12, "None");   // ₹10/tab
        var rx = await Product(c, "Azee Online", 3, 12, "H");          // ₹40/tab — flagged online but must never be listed
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, otc, ("ON1", 12, 5, 100, 70));
        await c.PurchaseAsync(supplier, rx, ("RX1", 12, 5, 120, 80));
        var anon = f.Factory.CreateClient();

        // module is off by default for every plan
        Assert.Equal(HttpStatusCode.NotFound, (await Public(anon, HttpMethod.Get, $"/api/v1/public/stores/{branch}")).Item1);
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, "/api/v1/online/orders"));
        await platform.Put($"/api/v1/platform/tenants/{tid}/subscription", new { planCode = "chain", status = "Active", extraModules = "online", migration = "Completed" });
        await c.Put($"/api/v1/online/settings/{branch}", new { enabled = true, deliveryPincodes = "400001, 400002", notice = "Orders reviewed by our pharmacist" });

        var (_, store) = await Public(anon, HttpMethod.Get, $"/api/v1/public/stores/{branch}");
        Assert.True(store.GetProperty("deliveryAvailable").GetBoolean());
        var (_, catalog) = await Public(anon, HttpMethod.Get, $"/api/v1/public/stores/{branch}/catalog");
        Assert.Equal(["Crocin Online"], catalog.EnumerateArray().Select(p => p.GetProperty("name").GetString()).ToArray());

        object Order(string pin, object[] lines, string phone = "9555500001") => new
        {
            name = "Kavita", phone, fulfilment = "Delivery", address = "12 MG Road", pincode = pin, requestText = "Azee 500 — 3 tablets as per prescription", lines,
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", Order("999999", [new { productId = otc, qtyUnits = 20 }]))).Item1);
        Assert.Equal(HttpStatusCode.BadRequest, (await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", Order("400001", [new { productId = rx, qtyUnits = 3 }]))).Item1);
        var (st, placed) = await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", Order("400001", [new { productId = otc, qtyUnits = 20 }]));
        Assert.Equal(HttpStatusCode.OK, st);
        var token = placed.GetProperty("trackingToken").GetString();
        var code = placed.GetProperty("handoverCode").GetString();

        var orders = await c.Get("/api/v1/online/orders?status=Submitted");
        var orderId = Assert.Single(orders.EnumerateArray()).GetProperty("id").GetGuid();
        object Review(string? prescriber) => new
        {
            accept = true, prescriberName = prescriber, notes = "Added Azee from prescription",
            lines = new object[] { new { productId = otc, approvedQtyUnits = 20 }, new { productId = rx, approvedQtyUnits = 3 } },
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/online/orders/{orderId}/review", Review("Dr. Shah"))).Status); // no prescription yet

        var bad = new MultipartFormDataContent { { new ByteArrayContent("not an image"u8.ToArray()), "file", "rx.txt" } };
        Assert.Equal(HttpStatusCode.BadRequest, (await anon.PostAsync($"/api/v1/public/orders/{token}/prescriptions", bad)).StatusCode);
        var good = new MultipartFormDataContent { { new ByteArrayContent(Png), "file", "rx.png" } };
        Assert.Equal(HttpStatusCode.OK, (await anon.PostAsync($"/api/v1/public/orders/{token}/prescriptions", good)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/online/orders/{orderId}/review", Review(null))).Status); // prescriber required
        var reviewed = await c.Post($"/api/v1/online/orders/{orderId}/review", Review("Dr. Shah"));
        Assert.Equal("NeedsCustomerApproval", reviewed.GetProperty("status").GetString()); // pharmacist added an item
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/online/orders/{orderId}/pack")).Status);
        var fileId = reviewed.GetProperty("files")[0].GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await c.Http.GetAsync($"/api/v1/online/orders/{orderId}/files/{fileId}")).StatusCode);

        var (_, approved) = await Public(anon, HttpMethod.Post, $"/api/v1/public/orders/{token}/approve");
        Assert.Equal("Accepted", approved.GetProperty("status").GetString());
        var packed = await c.Post($"/api/v1/online/orders/{orderId}/pack", new { });
        Assert.Equal("Packed", packed.GetProperty("status").GetString());
        Assert.Equal(320m, packed.GetProperty("amountDue").GetDecimal()); // 20 × ₹10 + 3 × ₹40
        Assert.Equal(30m, await c.StockUnitsAsync(otc));
        var customer = (await c.Get("/api/v1/customers/lookup?phone=9555500001")).GetProperty("balance").GetDecimal();
        Assert.Equal(320m, customer); // due until collected

        var agent = await c.AddUserAsync("DeliveryAgent");
        var agentId = (await c.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("role").GetString() == "DeliveryAgent").GetProperty("id").GetGuid();
        await c.Post($"/api/v1/online/orders/{orderId}/assign", new { agentUserId = agentId });
        var mine = Assert.Single((await agent.Get("/api/v1/online/deliveries")).EnumerateArray());
        Assert.Equal("9555500001", mine.GetProperty("customerPhone").GetString());
        Assert.Equal(0, mine.GetProperty("lines").GetArrayLength()); // agent doesn't see medicines
        Assert.Equal(HttpStatusCode.Forbidden, (await agent.Try(HttpMethod.Get, "/api/v1/pos/search?q=a")).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await agent.Try(HttpMethod.Post, $"/api/v1/online/orders/{orderId}/complete", new { handoverCode = "0000", mode = "Cash" })).Status);
        var done = await agent.Post($"/api/v1/online/orders/{orderId}/complete", new { handoverCode = code, mode = "Cash" });
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, done.GetProperty("customerPhone").ValueKind); // hidden after delivery
        Assert.Equal(0m, (await c.Get("/api/v1/customers/lookup?phone=9555500001")).GetProperty("balance").GetDecimal());
        var cod = await c.Get($"/api/v1/online/collections?date={DateTime.Today:yyyy-MM-dd}");
        Assert.Equal("DeliveryAgent", Assert.Single(cod.EnumerateArray()).GetProperty("agentName").GetString());

        // pickup order, store cancels after packing → bill cancelled, stock back
        var (_, p2) = await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", new
        {
            name = "Ravi", phone = "9555500002", fulfilment = "Pickup", lines = new[] { new { productId = otc, qtyUnits = 10 } },
        });
        var o2 = (await c.Get("/api/v1/online/orders?status=Submitted"))[0].GetProperty("id").GetGuid();
        Assert.Equal("Accepted", (await c.Post($"/api/v1/online/orders/{o2}/review", new { accept = true, lines = new[] { new { productId = otc, approvedQtyUnits = 10 } } })).GetProperty("status").GetString());
        Assert.Equal("ReadyForPickup", (await c.Post($"/api/v1/online/orders/{o2}/pack", new { })).GetProperty("status").GetString());
        Assert.Equal(20m, await c.StockUnitsAsync(otc));
        Assert.Equal(HttpStatusCode.NotFound, (await agent.Try(HttpMethod.Get, $"/api/v1/online/orders/{o2}")).Status); // not assigned to the agent
        await c.Post($"/api/v1/online/orders/{o2}/cancel", new { reason = "Customer didn't come" });
        Assert.Equal(30m, await c.StockUnitsAsync(otc));
        var (_, tracked) = await Public(anon, HttpMethod.Get, $"/api/v1/public/orders/{p2.GetProperty("trackingToken").GetString()}");
        Assert.Equal("Cancelled", tracked.GetProperty("status").GetString());

        // abuse limit: 5 orders per phone per day
        for (var i = 0; i < 4; i++)
            await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", new { name = "Spam", phone = "9555500003", fulfilment = "Pickup", lines = new[] { new { productId = otc, qtyUnits = 1 } } });
        Assert.Equal(HttpStatusCode.OK, (await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", new { name = "Spam", phone = "9555500003", fulfilment = "Pickup", lines = new[] { new { productId = otc, qtyUnits = 1 } } })).Item1);
        Assert.Equal((HttpStatusCode)429, (await Public(anon, HttpMethod.Post, $"/api/v1/public/stores/{branch}/orders", new { name = "Spam", phone = "9555500003", fulfilment = "Pickup", lines = new[] { new { productId = otc, qtyUnits = 1 } } })).Item1);

        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
