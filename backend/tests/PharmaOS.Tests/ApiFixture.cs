using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaOS.Core.Data;

namespace PharmaOS.Tests;

/// <summary>
/// Boots the real API against a throw-away SQL Server database (local default instance).
/// Every test registers its own tenant, so tests are isolated by tenant inside one DB.
/// </summary>
public sealed class ApiFixture : IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _dbName = $"PharmaOS_Test_{Guid.NewGuid():N}";
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;

    public Task InitializeAsync()
    {
        var server = Environment.GetEnvironmentVariable("PHARMA_TEST_SQL") ?? "localhost";
        // Windows dev: integrated login. CI/Linux: set PHARMA_TEST_SQL_AUTH="User Id=sa;Password=...".
        var auth = Environment.GetEnvironmentVariable("PHARMA_TEST_SQL_AUTH") ?? "Trusted_Connection=True";
        Environment.SetEnvironmentVariable("ConnectionStrings__Pharma",
            $"Server={server};Database={_dbName};{auth};TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("RateLimit__AuthPerMinute", "100000");
        Factory = new WebApplicationFactory<Program>(); // Development env → applies migrations on startup
        Factory.CreateClient().Dispose();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        using (var scope = Factory.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<PharmaDbContext>().Database.EnsureDeletedAsync();
        await Factory.DisposeAsync();
    }

    public async Task<TenantClient> NewTenantAsync(string code = "MAIN")
    {
        var client = Factory.CreateClient();
        var email = $"owner-{Guid.NewGuid():N}@test.local";
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            businessName = "Test Pharmacy", branchName = "Main Store", branchCode = code, stateCode = "27",
            ownerName = "Owner", email, password = "Password@123",
        }, Json);
        await TenantClient.EnsureOk(res);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new TenantClient(client, this);
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;

public sealed class TenantClient(HttpClient http, ApiFixture fixture)
{
    public HttpClient Http => http;

    public async Task<JsonElement> Post(string url, object body)
    {
        var res = await http.PostAsJsonAsync(url, body, ApiFixture.Json);
        await EnsureOk(res);
        return await Read(res);
    }

    public async Task<JsonElement> Put(string url, object body)
    {
        var res = await http.PutAsJsonAsync(url, body, ApiFixture.Json);
        await EnsureOk(res);
        return await Read(res);
    }

    public async Task<JsonElement> Delete(string url)
    {
        var res = await http.DeleteAsync(url);
        await EnsureOk(res);
        return await Read(res);
    }

    public async Task<JsonElement> Get(string url)
    {
        var res = await http.GetAsync(url);
        await EnsureOk(res);
        return await Read(res);
    }

    /// <summary>Sends and returns status + problem "code" without throwing.</summary>
    public async Task<(HttpStatusCode Status, string? Code)> Try(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body is not null) req.Content = JsonContent.Create(body, options: ApiFixture.Json);
        var res = await http.SendAsync(req);
        string? code = null;
        if (!res.IsSuccessStatusCode && res.Content.Headers.ContentLength != 0)
        {
            var text = await res.Content.ReadAsStringAsync();
            if (text.StartsWith('{') && JsonDocument.Parse(text).RootElement.TryGetProperty("code", out var c)) code = c.GetString();
        }
        return (res.StatusCode, code);
    }

    public async Task<TenantClient> AddUserAsync(string role, string? displayName = null)
    {
        var email = $"{role.ToLower()}-{Guid.NewGuid():N}@test.local";
        await Post("/api/v1/users", new { email, displayName = displayName ?? role, password = "Password@123", role });
        var c = fixture.Factory.CreateClient();
        var res = await c.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password@123" }, ApiFixture.Json);
        await EnsureOk(res);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new TenantClient(c, fixture);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage res) =>
        res.StatusCode == HttpStatusCode.NoContent ? default : await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);

    public static async Task EnsureOk(HttpResponseMessage res)
    {
        if (!res.IsSuccessStatusCode)
            throw new Xunit.Sdk.XunitException($"{(int)res.StatusCode} {res.RequestMessage?.Method} {res.RequestMessage?.RequestUri}: {await res.Content.ReadAsStringAsync()}");
    }

    // ---------- domain helpers ----------

    public async Task<Guid> CreateProductAsync(string name, decimal packSize = 10, decimal gst = 12, bool loose = true, string schedule = "None")
    {
        var p = await Post("/api/v1/products", new
        {
            name, composition = "Paracetamol 500mg", manufacturer = "Acme", hsnCode = "3004", gstRate = gst, packSize,
            packUnit = "Strip", baseUnit = "Tab", allowLooseSale = loose, schedule, reorderLevelUnits = 20, isActive = true,
        });
        return p.GetProperty("id").GetGuid();
    }

    public async Task<Guid> CreateSupplierAsync(string name = "Shree Distributors")
        => (await Post("/api/v1/suppliers", new { name = $"{name} {Guid.NewGuid():N}"[..30], paymentTermsDays = 30, isActive = true }))
            .GetProperty("id").GetGuid();

    public static string Expiry(int monthsFromNow) => DateTime.Today.AddMonths(monthsFromNow).ToString("yyyy-MM-01");

    /// <summary>Creates and posts a purchase; each batch tuple is (batchNo, monthsToExpiry, packs, mrp, rate).</summary>
    public async Task<JsonElement> PurchaseAsync(Guid supplierId, Guid productId, params (string Batch, int Months, decimal Packs, decimal Mrp, decimal Rate)[] batches)
    {
        var draft = await Post("/api/v1/purchases", new
        {
            supplierId, supplierInvoiceNo = $"INV-{Guid.NewGuid():N}"[..20], invoiceDate = DateTime.Today.ToString("yyyy-MM-dd"),
            otherCharges = 0,
            lines = batches.Select(b => new
            {
                productId, batchNo = b.Batch, expiryDate = Expiry(b.Months), mrp = b.Mrp, rate = b.Rate,
                qtyPacks = b.Packs, freeQtyPacks = 0, discountPercent = 0,
            }).ToArray(),
        });
        return await Post($"/api/v1/purchases/{draft.GetProperty("id").GetGuid()}/post", new { });
    }

    public Task<JsonElement> SellAsync(Guid productId, decimal qtyUnits, decimal amount, Guid? requestId = null, Guid? batchId = null,
        decimal discount = 0) =>
        Post("/api/v1/sales", new
        {
            clientRequestId = requestId ?? Guid.NewGuid(),
            lines = new[] { new { productId, batchId, qtyUnits, discountPercent = discount } },
            payments = new[] { new { mode = "Cash", amount } },
        });

    public async Task<decimal> StockUnitsAsync(Guid productId)
    {
        var page = await Get($"/api/v1/stock?q=&pageSize=200");
        foreach (var row in page.GetProperty("items").EnumerateArray())
            if (row.GetProperty("productId").GetGuid() == productId) return row.GetProperty("qtyUnits").GetDecimal();
        return 0;
    }
}

public static class DbAssert
{
    /// <summary>Inventory invariant: ledger sum per branch+batch equals the balance projection, across all tenants.</summary>
    public static async Task LedgerMatchesBalancesAsync(ApiFixture f)
    {
        using var scope = f.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PharmaDbContext>();
        var ledger = await db.StockMovements.IgnoreQueryFilters()
            .GroupBy(m => new { m.BranchId, m.BatchId }).Select(g => new { g.Key.BranchId, g.Key.BatchId, Qty = g.Sum(x => x.QtyUnits) })
            .ToListAsync();
        var balances = await db.StockBalances.IgnoreQueryFilters().ToDictionaryAsync(b => (b.BranchId, b.BatchId), b => b.QtyUnits);
        foreach (var l in ledger)
            Assert.Equal(l.Qty, balances.GetValueOrDefault((l.BranchId, l.BatchId)));
        Assert.Equal(balances.Count(b => b.Value != 0), ledger.Count(l => l.Qty != 0));
    }
}
