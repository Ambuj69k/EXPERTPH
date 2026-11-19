using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Manufacturing &amp; QA: approved recipe, QC-gated material lots, genealogy, quarantine, e-signatures, separation of duties, books.</summary>
[Collection("api")]
public class ManufacturingTests(ApiFixture f)
{
    private const string Pw = "Password@123";
    private static string Today => DateTime.Today.ToString("yyyy-MM-dd");

    private async Task SetPlan(TenantClient c, string plan)
    {
        var tid = (await c.Get("/api/v1/me")).GetProperty("tenantId").GetGuid();
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        await new TenantClient(http, f).Put($"/api/v1/platform/tenants/{tid}/subscription", new { planCode = plan, status = "Active", migration = "Completed" });
    }

    private static async Task<Guid> LotId(TenantClient c, string batchNo) =>
        (await c.Get($"/api/v1/mfg/lots?q={batchNo}")).EnumerateArray().Single(x => x.GetProperty("batchNo").GetString() == batchNo).GetProperty("batchId").GetGuid();

    [Fact]
    public async Task Manufacturing_and_qa_end_to_end()
    {
        var c = await f.NewTenantAsync();
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, "/api/v1/mfg/recipes"));
        await SetPlan(c, "manufacturing");
        var fg = await c.CreateProductAsync("Para Mfg 500");
        var api = await c.CreateProductAsync("API Paracetamol");
        var starch = await c.CreateProductAsync("Maize Starch");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, api, ("API-A", 12, 10, 100, 70));     // 100 units at cost 7
        await c.PurchaseAsync(supplier, starch, ("ST-1", 12, 10, 100, 50));   // 100 units at cost 5

        var prod = await c.AddUserAsync("Production", "Prakash Prod");
        var qc = await c.AddUserAsync("QcAnalyst", "Kavya QC");
        var qa = await c.AddUserAsync("QaManager", "Anil QA");

        // recipe: drafted by production, approved by QA with an e-signature
        var recipe = await prod.Post("/api/v1/mfg/recipes", new { productId = fg, batchSizeUnits = 100, lines = new[] { new { materialProductId = api, qtyUnits = 50m }, new { materialProductId = starch, qtyUnits = 20m } } });
        var recipeId = recipe.GetProperty("id").GetGuid();
        object Plan(string no) => new { recipeId, batchNo = no, expiryDate = DateTime.Today.AddYears(2).ToString("yyyy-MM-dd"), mrp = 30, plannedUnits = 100 };
        Assert.Equal((HttpStatusCode.Conflict, "recipe_not_approved"), await prod.Try(HttpMethod.Post, "/api/v1/mfg/batches", Plan("PB-1")));
        Assert.Equal(HttpStatusCode.Forbidden, (await prod.Try(HttpMethod.Post, $"/api/v1/mfg/recipes/{recipeId}/approve", new { password = Pw })).Status);
        Assert.Equal((HttpStatusCode.Forbidden, "invalid_signature"), await qa.Try(HttpMethod.Post, $"/api/v1/mfg/recipes/{recipeId}/approve", new { password = "wrong" }));
        Assert.Equal("Approved", (await qa.Post($"/api/v1/mfg/recipes/{recipeId}/approve", new { password = Pw })).GetProperty("status").GetString());

        // API material has a spec → its lots must pass QC before use
        var apiSpec = await qc.Post("/api/v1/mfg/specs", new { productId = api, tests = new[] { new { name = "Assay", unit = "%", min = 98m, max = 102m } } });
        await qa.Post($"/api/v1/mfg/specs/{apiSpec.GetProperty("id").GetGuid()}/approve", new { password = Pw });
        var pb = await prod.Post("/api/v1/mfg/batches", Plan("PB-1"));
        var pbId = pb.GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_batch"), await prod.Try(HttpMethod.Post, "/api/v1/mfg/batches", Plan("pb-1")));
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_material"), await prod.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pbId}/issue-materials"));

        var apiLot = await LotId(c, "API-A");
        var s1 = await qc.Post("/api/v1/mfg/samples", new { batchId = apiLot });
        var assayTest = s1.GetProperty("tests")[0].GetProperty("id").GetGuid();
        s1 = await qc.Post($"/api/v1/mfg/samples/{s1.GetProperty("id").GetGuid()}/results", new { password = Pw, results = new[] { new { qcSpecTestId = assayTest, value = 99.5m } } });
        Assert.Equal("Passed", s1.GetProperty("status").GetString());
        Assert.Equal("Tested by Kavya QC (QC)", s1.GetProperty("testerSignature").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), await qc.Try(HttpMethod.Post, $"/api/v1/mfg/samples/{s1.GetProperty("id").GetGuid()}/results",
            new { password = Pw, results = new[] { new { qcSpecTestId = assayTest, value = 101m } } }));

        // materials issued (genealogy recorded), output booked into quarantine
        pb = await prod.Post($"/api/v1/mfg/batches/{pbId}/issue-materials", new { });
        Assert.Equal("InProcess", pb.GetProperty("status").GetString());
        Assert.Equal(450m, pb.GetProperty("materialCost").GetDecimal()); // 50 × 7 + 20 × 5
        Assert.Equal(50m, await c.StockUnitsAsync(api));
        pb = await prod.Post($"/api/v1/mfg/batches/{pbId}/output", new { outputUnits = 90 });
        Assert.Equal("Quarantine", pb.GetProperty("status").GetString());
        Assert.Equal(90m, pb.GetProperty("yieldPercent").GetDecimal());
        var fgLot = pb.GetProperty("outputBatchId").GetGuid();
        Assert.Equal(HttpStatusCode.Conflict, (await c.Try(HttpMethod.Post, "/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), lines = new[] { new { productId = fg, batchId = fgLot, qtyUnits = 1m, discountPercent = 0m } }, payments = new[] { new { mode = "Cash", amount = 3m } },
        })).Status); // quarantined stock cannot be sold

        // release needs a passed QC on the finished batch
        Assert.Equal((HttpStatusCode.Conflict, "qc_not_passed"), await qa.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pbId}/release", new { password = Pw }));
        var fgSpec = await qc.Post("/api/v1/mfg/specs", new
        {
            productId = fg, tests = new object[] { new { name = "Dissolution", unit = "%", min = 80m }, new { name = "Description", requirement = "White round tablets" } },
        });
        await qa.Post($"/api/v1/mfg/specs/{fgSpec.GetProperty("id").GetGuid()}/approve", new { password = Pw });
        async Task<JsonElement> Test(TenantClient who, decimal dissolution)
        {
            var s = await qc.Post("/api/v1/mfg/samples", new { batchId = fgLot });
            var t = s.GetProperty("tests");
            return await who.Post($"/api/v1/mfg/samples/{s.GetProperty("id").GetGuid()}/results", new
            {
                password = Pw, results = new object[]
                {
                    new { qcSpecTestId = t[0].GetProperty("id").GetGuid(), value = dissolution, pass = true }, // limits decide, not the checkbox
                    new { qcSpecTestId = t[1].GetProperty("id").GetGuid(), text = "White, round, uncoated", pass = true },
                },
            });
        }
        Assert.Equal("Failed", (await Test(qc, 75m)).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "qc_not_passed"), await qa.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pbId}/release", new { password = Pw }));
        Assert.Equal("Passed", (await Test(c, 86m)).GetProperty("status").GetString()); // retest signed by the owner

        Assert.Equal(HttpStatusCode.Forbidden, (await qc.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pbId}/release", new { password = Pw })).Status);
        Assert.Equal((HttpStatusCode.Conflict, "same_person"), await c.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pbId}/release", new { password = Pw }));
        pb = await qa.Post($"/api/v1/mfg/batches/{pbId}/release", new { password = Pw, note = "BMR reviewed" });
        Assert.Equal("Released", pb.GetProperty("status").GetString());
        Assert.Equal("Released for sale by Anil QA (QA)", pb.GetProperty("decisionSignature").GetString());
        await c.SellAsync(fg, 10, 30, batchId: fgLot); // now sellable

        // genealogy both ways
        var back = await prod.Get($"/api/v1/mfg/genealogy/{fgLot}");
        Assert.False(back.GetProperty("isBlocked").GetBoolean());
        Assert.Equal(new[] { "API-A", "ST-1" }, back.GetProperty("madeFrom").EnumerateArray().Select(x => x.GetProperty("batchNo").GetString()).OrderBy(x => x).ToArray());
        Assert.Equal(2, back.GetProperty("samples").GetArrayLength()); // failed + passed
        var fwd = await qa.Get($"/api/v1/mfg/genealogy/{apiLot}");
        Assert.Equal("PB-1", Assert.Single(fwd.GetProperty("usedIn").EnumerateArray()).GetProperty("batchNo").GetString());

        // rejection keeps the lot blocked
        var pb2 = (await prod.Post("/api/v1/mfg/batches", Plan("PB-2"))).GetProperty("id").GetGuid();
        await prod.Post($"/api/v1/mfg/batches/{pb2}/issue-materials", new { });
        var out2 = await prod.Post($"/api/v1/mfg/batches/{pb2}/output", new { outputUnits = 100 });
        Assert.Equal(HttpStatusCode.BadRequest, (await qa.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pb2}/reject", new { password = Pw })).Status);
        Assert.Equal("Rejected", (await qa.Post($"/api/v1/mfg/batches/{pb2}/reject", new { password = Pw, note = "Hardness out of limits" })).GetProperty("status").GetString());
        Assert.Contains("Rejected by QA", (await qa.Get($"/api/v1/mfg/genealogy/{out2.GetProperty("outputBatchId").GetGuid()}")).GetProperty("blockReason").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_material"), await prod.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{(await prod.Post("/api/v1/mfg/batches", Plan("PB-3"))).GetProperty("id").GetGuid()}/issue-materials"));

        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        foreach (var chk in (await c.Get("/api/v1/accounting/control-checks")).EnumerateArray())
            Assert.True(chk.GetProperty("difference").GetDecimal() == 0, $"{chk.GetProperty("name").GetString()} off by {chk.GetProperty("difference")}");
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
