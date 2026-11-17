using System.Net;
using System.Text.Json;
using PharmaOS.Core.Services;

namespace PharmaOS.Tests;

/// <summary>Reorder suggestions, purchase orders with partial receipts, CSV import with learned item mapping.</summary>
[Collection("api")]
public class Phase5Tests(ApiFixture f)
{
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private static object Purchase(Guid supplier, Guid product, decimal packs, decimal free = 0, Guid? orderId = null) => new
    {
        supplierId = supplier, supplierInvoiceNo = $"INV-{Guid.NewGuid():N}"[..18], invoiceDate = Today, otherCharges = 0, purchaseOrderId = orderId,
        lines = new[] { new { productId = product, batchNo = "PO1", expiryDate = TenantClient.Expiry(12), mrp = 100, rate = 70, qtyPacks = packs, freeQtyPacks = free, discountPercent = 0 } },
    };

    private static async Task<JsonElement> PostPurchase(TenantClient c, object body)
    {
        var d = await c.Post("/api/v1/purchases", body);
        return await c.Post($"/api/v1/purchases/{d.GetProperty("id").GetGuid()}/post", new { });
    }

    [Fact]
    public async Task Reorder_formula_and_open_orders()
    {
        var c = await f.NewTenantAsync();
        var a = await c.CreateProductAsync("Reorder A");       // pack 10, reorder level 20 units
        var b = await c.CreateProductAsync("Never Stocked B"); // no stock, reorder level 20
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, a, ("R1", 12, 5, 100, 70));
        await c.SellAsync(a, 40, 400); // on hand 10

        var s = await c.Get("/api/v1/purchases/reorder?lookbackDays=30&coverDays=15");
        var ra = s.EnumerateArray().Single(x => x.GetProperty("productId").GetGuid() == a);
        Assert.Equal(1.3333m, ra.GetProperty("avgDailyUnits").GetDecimal());
        Assert.Equal(20m, ra.GetProperty("targetUnits").GetDecimal()); // max(ceil(1.333×15)=20, 20)
        Assert.Equal(1m, ra.GetProperty("suggestedPacks").GetDecimal()); // (20 − 10) / 10
        Assert.Equal(supplier, ra.GetProperty("lastSupplierId").GetGuid());
        Assert.Equal(70m, ra.GetProperty("lastRate").GetDecimal());
        var rb = s.EnumerateArray().Single(x => x.GetProperty("productId").GetGuid() == b);
        Assert.Equal(2m, rb.GetProperty("suggestedPacks").GetDecimal());

        var po = await c.Post("/api/v1/purchase-orders", new { supplierId = supplier, lines = new[] { new { productId = a, qtyPacks = 3, expectedRate = 70 } } });
        Assert.StartsWith("OMAIN", po.GetProperty("poNo").GetString());
        // a Draft order is not yet "on order"
        Assert.Contains((await c.Get("/api/v1/purchases/reorder")).EnumerateArray(), x => x.GetProperty("productId").GetGuid() == a);
        await c.Post($"/api/v1/purchase-orders/{po.GetProperty("id").GetGuid()}/send", new { });
        Assert.DoesNotContain((await c.Get("/api/v1/purchases/reorder")).EnumerateArray(), x => x.GetProperty("productId").GetGuid() == a);
    }

    [Fact]
    public async Task Purchase_order_partial_receipts_cancel_and_close()
    {
        var c = await f.NewTenantAsync();
        var a = await c.CreateProductAsync("Ordered A");
        var supplier = await c.CreateSupplierAsync();
        var other = await c.CreateSupplierAsync("Other");
        var po = await c.Post("/api/v1/purchase-orders", new { supplierId = supplier, lines = new[] { new { productId = a, qtyPacks = 3, expectedRate = 70 } } });
        var id = po.GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Conflict, "order_not_open"), await c.Try(HttpMethod.Post, "/api/v1/purchases", Purchase(supplier, a, 1, orderId: id))); // still Draft
        await c.Post($"/api/v1/purchase-orders/{id}/send", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/purchases", Purchase(other, a, 1, orderId: id))).Status);

        await PostPurchase(c, Purchase(supplier, a, 1, orderId: id));
        var o1 = await c.Get($"/api/v1/purchase-orders/{id}");
        Assert.Equal("PartiallyReceived", o1.GetProperty("status").GetString());
        Assert.Equal(2m, o1.GetProperty("lines")[0].GetProperty("pendingQtyPacks").GetDecimal());

        var second = await PostPurchase(c, Purchase(supplier, a, 2, free: 1, orderId: id)); // free goods don't count towards the order
        var o2 = await c.Get($"/api/v1/purchase-orders/{id}");
        Assert.Equal("Received", o2.GetProperty("status").GetString());
        Assert.Equal(3m, o2.GetProperty("lines")[0].GetProperty("receivedQtyPacks").GetDecimal());
        Assert.Equal(2, o2.GetProperty("receipts").GetArrayLength());

        await c.Post($"/api/v1/purchases/{second.GetProperty("id").GetGuid()}/cancel", new { reason = "wrong" });
        Assert.Equal("PartiallyReceived", (await c.Get($"/api/v1/purchase-orders/{id}")).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "has_receipts"), await c.Try(HttpMethod.Post, $"/api/v1/purchase-orders/{id}/cancel"));
        Assert.Equal("Closed", (await c.Post($"/api/v1/purchase-orders/{id}/close", new { })).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "order_not_open"), await c.Try(HttpMethod.Post, "/api/v1/purchases", Purchase(supplier, a, 1, orderId: id)));
    }

    [Fact]
    public async Task Csv_import_preview_learns_supplier_item_codes()
    {
        var c = await f.NewTenantAsync();
        var dolo = await c.CreateProductAsync("Dolo 650");
        var supplier = await c.CreateSupplierAsync();
        const string csv = "Item Code,Item Description,Batch No,Exp,Qty,Free,MRP,PTR,Disc %,GST %\r\n" +
                           "D650,DOLO-650 TAB,b1,03/28,10,1,30.91,22,5,12\r\n" +
                           "X1,\"Unknown Syrup, 100ml\",S9,Mar-2028,2,0,120,80,0,12\r\n" +
                           ",,,,,,,,,\r\n";

        var p1 = await c.Post("/api/v1/purchases/import/preview", new { supplierId = supplier, csvText = csv, saveMapping = true });
        Assert.Equal("Item Description", p1.GetProperty("mapping").GetProperty("itemName").GetString());
        Assert.Equal("PTR", p1.GetProperty("mapping").GetProperty("rate").GetString());
        var rows = p1.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(2, rows.Count);
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("productId").ValueKind);
        Assert.Contains(rows[0].GetProperty("suggestions").EnumerateArray(), s => s.GetProperty("productId").GetGuid() == dolo);
        Assert.Equal("B1", rows[0].GetProperty("batchNo").GetString());
        Assert.Equal("2028-03-31", rows[0].GetProperty("expiryDate").GetString());
        Assert.Equal(10m, rows[0].GetProperty("qtyPacks").GetDecimal());
        Assert.Equal(1m, rows[0].GetProperty("freeQtyPacks").GetDecimal());
        Assert.Equal(5m, rows[0].GetProperty("discountPercent").GetDecimal());
        Assert.Equal("Unknown Syrup, 100ml", rows[1].GetProperty("itemName").GetString());
        Assert.Equal("2028-03-31", rows[1].GetProperty("expiryDate").GetString());

        await c.Post($"/api/v1/suppliers/{supplier}/item-map", new[] { new { supplierItemCode = "D650", supplierItemName = "DOLO-650 TAB", productId = dolo } });
        var p2 = await c.Post("/api/v1/purchases/import/preview", new { supplierId = supplier, csvText = csv.Replace("DOLO-650 TAB", "Dolo 650mg Tablet"), saveMapping = false });
        var r0 = p2.GetProperty("rows")[0];
        Assert.Equal(dolo, r0.GetProperty("productId").GetGuid());
        Assert.Equal("supplier code", r0.GetProperty("matchedBy").GetString()); // matched by code even though the name changed
        Assert.Equal(1, p2.GetProperty("matched").GetInt32());

        // reviewed rows become a DRAFT purchase only
        var draft = await c.Post("/api/v1/purchases", new
        {
            supplierId = supplier, supplierInvoiceNo = "CSV-1", invoiceDate = Today, otherCharges = 0,
            lines = new[] { new { productId = dolo, batchNo = "B1", expiryDate = "2028-03-31", mrp = 30.91, rate = 22, qtyPacks = 10, freeQtyPacks = 1, discountPercent = 5 } },
        });
        Assert.Equal("Draft", draft.GetProperty("status").GetString());
        Assert.Equal(0m, await c.StockUnitsAsync(dolo));

        Assert.Equal((HttpStatusCode.NotImplemented, "ai_not_configured"),
            await c.Try(HttpMethod.Post, $"/api/v1/purchases/import/extract?supplierId={supplier}"));
    }

    [Theory]
    [InlineData("03/28", "2028-03-31")]
    [InlineData("3/2028", "2028-03-31")]
    [InlineData("Feb-28", "2028-02-29")]
    [InlineData("FEB 2027", "2027-02-28")]
    [InlineData("15/11/2027", "2027-11-30")]
    [InlineData("2027-11-15", "2027-11-30")]
    [InlineData("13/27", null)]
    [InlineData("soon", null)]
    public void Expiry_formats(string input, string? expected) =>
        Assert.Equal(expected, ImportService.ParseExpiry(input)?.ToString("yyyy-MM-dd"));

    [Fact]
    public void Csv_parser_handles_quotes_and_line_endings()
    {
        var rows = ImportService.ParseCsv("a,\"b, c\",\"say \"\"hi\"\"\"\nx,y,z\r\n");
        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b, c", "say \"hi\""], rows[0]);
        Assert.Equal("DOLO 650 TAB", ImportService.Normalize(" dolo-650   tab. "));
    }
}
