using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Follow-up features: distributor dispatch, MR expenses &amp; targets, controlled-drug register, deviations/CAPA.</summary>
[Collection("api")]
public class OperationsGapsTests(ApiFixture f)
{
    private const string Pw = "Password@123";
    private static string D(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string Today => D(DateTime.Today);

    private async Task SetPlan(TenantClient c, string plan)
    {
        var tid = (await c.Get("/api/v1/me")).GetProperty("tenantId").GetGuid();
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        await new TenantClient(http, f).Put($"/api/v1/platform/tenants/{tid}/subscription", new { planCode = plan, status = "Active", migration = "Completed" });
    }

    private static async Task<Guid> UserId(TenantClient owner, string name) =>
        (await owner.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("displayName").GetString() == name).GetProperty("id").GetGuid();

    [Fact]
    public async Task Distributor_dispatch_pick_list_and_delivery()
    {
        var c = await f.NewTenantAsync();
        await SetPlan(c, "distributor");
        var p = await c.CreateProductAsync("Dispatch Tab");
        await c.PurchaseAsync(await c.CreateSupplierAsync(), p, ("DS1", 12, 50, 100, 70));
        await c.Put("/api/v1/distribution/rates", new { productId = p, retailerId = (Guid?)null, ratePerPack = 80 });
        async Task<Guid> Retailer(string n, string route) => (await c.Post("/api/v1/distribution/retailers", new { name = n, stateCode = "27", route, creditLimit = 0, creditDays = 30, isActive = true })).GetProperty("id").GetGuid();
        async Task<JsonElement> Invoice(Guid r, decimal packs) => await c.Post("/api/v1/distribution/invoices", new { clientRequestId = Guid.NewGuid(), retailerId = r, lines = new[] { new { productId = p, qtyPacks = packs } } });
        var a = await Invoice(await Retailer("Alpha Med", "R1"), 3);
        var b = await Invoice(await Retailer("Beta Med", "R2"), 2);

        Assert.Equal(2, (await c.Get("/api/v1/distribution/dispatch/pending")).GetArrayLength());
        var trip = await c.Post("/api/v1/distribution/dispatches", new { route = "West", vehicle = "mh01ab1234", driverName = "Raju", invoiceIds = new[] { a.GetProperty("id").GetGuid(), b.GetProperty("id").GetGuid() } });
        var tripId = trip.GetProperty("id").GetGuid();
        Assert.Equal("MH01AB1234", trip.GetProperty("vehicle").GetString());
        Assert.Equal(0, (await c.Get("/api/v1/distribution/dispatch/pending")).GetArrayLength());
        Assert.Equal((HttpStatusCode.Conflict, "not_dispatchable"), await c.Try(HttpMethod.Post, "/api/v1/distribution/dispatches", new { invoiceIds = new[] { a.GetProperty("id").GetGuid() } }));

        var pick = Assert.Single((await c.Get($"/api/v1/distribution/dispatches/{tripId}/pick-list")).EnumerateArray());
        Assert.Equal(5m, pick.GetProperty("packs").GetDecimal());
        Assert.Equal(2, pick.GetProperty("invoices").GetArrayLength());

        var stops = trip.GetProperty("stops");
        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), await c.Try(HttpMethod.Post, $"/api/v1/distribution/dispatches/{tripId}/stops/{stops[0].GetProperty("id").GetGuid()}", new { delivered = true, receivedBy = "x" }));
        await c.Post($"/api/v1/distribution/dispatches/{tripId}/depart", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/distribution/dispatches/{tripId}/stops/{stops[0].GetProperty("id").GetGuid()}", new { delivered = true })).Status);
        await c.Post($"/api/v1/distribution/dispatches/{tripId}/stops/{stops[0].GetProperty("id").GetGuid()}", new { delivered = true, receivedBy = "Shop owner" });
        trip = await c.Post($"/api/v1/distribution/dispatches/{tripId}/stops/{stops[1].GetProperty("id").GetGuid()}", new { delivered = false, note = "Shop closed" });
        Assert.Equal("Completed", trip.GetProperty("status").GetString());

        var pending = Assert.Single((await c.Get("/api/v1/distribution/dispatch/pending")).EnumerateArray()); // failed one comes back
        Assert.True(pending.GetProperty("previouslyFailed").GetBoolean());
        Assert.Equal(stops[1].GetProperty("invoiceNo").GetString(), pending.GetProperty("invoiceNo").GetString());
    }

    [Fact]
    public async Task Mr_expenses_and_targets()
    {
        var c = await f.NewTenantAsync();
        await SetPlan(c, "fieldforce");
        var mgr = await c.AddUserAsync("AreaManager", "Exp Manager");
        var rep = await c.AddUserAsync("MedicalRep", "Exp Rep");
        var other = await c.AddUserAsync("AreaManager", "Other Manager");
        var repId = await UserId(c, "Exp Rep");
        var t = (await c.Post("/api/v1/sfa/territories", new { name = "Exp T", repUserId = repId, managerUserId = await UserId(c, "Exp Manager"), isActive = true })).GetProperty("id").GetGuid();

        Assert.Equal((HttpStatusCode.Conflict, "future_date"), await rep.Try(HttpMethod.Post, "/api/v1/sfa/expenses", new { date = D(DateTime.Today.AddDays(1)), category = "Travel", amount = 100 }));
        Assert.Equal((HttpStatusCode.Conflict, "outside_window"), await rep.Try(HttpMethod.Post, "/api/v1/sfa/expenses", new { date = D(DateTime.Today.AddDays(-40)), category = "Travel", amount = 100 }));
        var e1 = (await rep.Post("/api/v1/sfa/expenses", new { date = Today, category = "Travel", amount = 240.5m, km = 48, note = "Bike" })).GetProperty("id").GetGuid();
        var e2 = (await rep.Post("/api/v1/sfa/expenses", new { date = Today, category = "Food", amount = 150 })).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await rep.Try(HttpMethod.Post, $"/api/v1/sfa/expenses/{e1}/approve", new { })).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await other.Try(HttpMethod.Post, $"/api/v1/sfa/expenses/{e1}/approve", new { })).Status); // not their MR
        await mgr.Post($"/api/v1/sfa/expenses/{e1}/approve", new { });
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.Try(HttpMethod.Post, $"/api/v1/sfa/expenses/{e2}/reject", new { comment = "" })).Status);
        await mgr.Post($"/api/v1/sfa/expenses/{e2}/reject", new { comment = "No bill attached" });

        await mgr.Put("/api/v1/sfa/targets", new { repUserId = repId, month = Today, callsTarget = 1, coverageTargetPercent = 100 });
        var doc = (await rep.Post("/api/v1/sfa/doctors", new { name = "Dr Target", territoryId = t, @class = "C", isActive = true })).GetProperty("id").GetGuid();
        var day = await rep.Post("/api/v1/sfa/reports/open", new { date = Today });
        day = await rep.Post($"/api/v1/sfa/reports/{day.GetProperty("id").GetGuid()}/calls", new { clientRequestId = Guid.NewGuid(), doctorId = doc });
        await rep.Post($"/api/v1/sfa/reports/{day.GetProperty("id").GetGuid()}/submit", new { version = day.GetProperty("version").GetInt32() });

        var row = Assert.Single((await mgr.Get($"/api/v1/sfa/scorecard?month={Today}")).EnumerateArray());
        Assert.Equal((1, 1, true, true), (row.GetProperty("callsTarget").GetInt32(), row.GetProperty("calls").GetInt32(), row.GetProperty("callsMet").GetBoolean(), row.GetProperty("coverageMet").GetBoolean()));
        Assert.Equal((240.5m, 0m), (row.GetProperty("expensesApproved").GetDecimal(), row.GetProperty("expensesPending").GetDecimal()));
        Assert.Equal(2, (await rep.Get($"/api/v1/sfa/expenses?month={Today}")).GetArrayLength());
    }

    [Fact]
    public async Task Controlled_drug_register_tracks_every_movement()
    {
        var c = await f.NewTenantAsync();
        var morphine = await c.CreateProductAsync("Morphine Reg", schedule: "NarcoticPsychotropic");
        var plain = await c.CreateProductAsync("Plain Reg");
        var s = await c.CreateSupplierAsync();
        await c.PurchaseAsync(s, morphine, ("MR1", 12, 2, 100, 80));
        await c.PurchaseAsync(s, plain, ("PL1", 12, 2, 100, 80));
        await c.Post("/api/v1/sales", new
        {
            clientRequestId = Guid.NewGuid(), customer = new { name = "Patient A", phone = "9800000001" }, doctorName = "Dr Rao (Reg 1234)",
            lines = new[] { new { productId = morphine, qtyUnits = 3m, discountPercent = 0m } }, payments = new[] { new { mode = "Cash", amount = 30m } },
        });
        var reg = await c.Get($"/api/v1/reports/controlled-drugs?from={Today}&to={Today}");
        var b = Assert.Single(reg.EnumerateArray()); // plain items are not in the register
        Assert.Equal(("MR1", 0m, 20m, 3m, 17m), (b.GetProperty("batchNo").GetString(), b.GetProperty("opening").GetDecimal(), b.GetProperty("received").GetDecimal(),
            b.GetProperty("issued").GetDecimal(), b.GetProperty("closing").GetDecimal()));
        var sale = b.GetProperty("entries").EnumerateArray().Single(e => e.GetProperty("out").GetDecimal() == 3m);
        Assert.Equal("Patient A", sale.GetProperty("party").GetString());
        Assert.Equal("Dr Rao (Reg 1234)", sale.GetProperty("prescriber").GetString());
        Assert.Equal(17m, sale.GetProperty("balance").GetDecimal());
        var cashier = await c.AddUserAsync("Cashier");
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, $"/api/v1/reports/controlled-drugs?from={Today}&to={Today}")).Status);
    }

    [Fact]
    public async Task Open_major_deviation_blocks_release_until_capa_closed()
    {
        var c = await f.NewTenantAsync();
        await SetPlan(c, "manufacturing");
        var fg = await c.CreateProductAsync("Dev FG");
        var mat = await c.CreateProductAsync("Dev Material");
        await c.PurchaseAsync(await c.CreateSupplierAsync(), mat, ("DM1", 12, 5, 100, 70));
        var qc = await c.AddUserAsync("QcAnalyst", "Dev QC");
        var qa = await c.AddUserAsync("QaManager", "Dev QA");
        var recipe = (await c.Post("/api/v1/mfg/recipes", new { productId = fg, batchSizeUnits = 10, lines = new[] { new { materialProductId = mat, qtyUnits = 10m } } })).GetProperty("id").GetGuid();
        await qa.Post($"/api/v1/mfg/recipes/{recipe}/approve", new { password = Pw });
        var spec = (await qc.Post("/api/v1/mfg/specs", new { productId = fg, tests = new[] { new { name = "Weight", min = 90m, max = 110m } } })).GetProperty("id").GetGuid();
        await qa.Post($"/api/v1/mfg/specs/{spec}/approve", new { password = Pw });
        var pb = (await c.Post("/api/v1/mfg/batches", new { recipeId = recipe, batchNo = "DV-1", expiryDate = D(DateTime.Today.AddYears(1)), mrp = 50, plannedUnits = 10 })).GetProperty("id").GetGuid();
        await c.Post($"/api/v1/mfg/batches/{pb}/issue-materials", new { });
        var lot = (await c.Post($"/api/v1/mfg/batches/{pb}/output", new { outputUnits = 10 })).GetProperty("outputBatchId").GetGuid();
        var sample = await qc.Post("/api/v1/mfg/samples", new { batchId = lot });
        await qc.Post($"/api/v1/mfg/samples/{sample.GetProperty("id").GetGuid()}/results", new { password = Pw, results = new[] { new { qcSpecTestId = sample.GetProperty("tests")[0].GetProperty("id").GetGuid(), value = 100m } } });

        var minor = await qc.Post("/api/v1/mfg/deviations", new { productionBatchId = pb, title = "Late entry", description = "Log signed 10 min late", severity = "Minor" });
        var major = await c.Post("/api/v1/mfg/deviations", new { productionBatchId = pb, title = "Mixer stopped", description = "Mixing interrupted 5 min", severity = "Major" });
        var majorId = major.GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Conflict, "open_deviation"), await qa.Try(HttpMethod.Post, $"/api/v1/mfg/batches/{pb}/release", new { password = Pw }));
        Assert.Equal((HttpStatusCode.Conflict, "invalid_state"), await qa.Try(HttpMethod.Post, $"/api/v1/mfg/deviations/{majorId}/close", new { password = Pw }));
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, $"/api/v1/mfg/deviations/{majorId}/investigation", new { rootCause = "", correctiveAction = "", version = major.GetProperty("version").GetInt32() })).Status);
        major = await c.Post($"/api/v1/mfg/deviations/{majorId}/investigation", new
        {
            rootCause = "Power dip tripped the mixer", correctiveAction = "Re-mixed per SOP; uniformity re-tested", preventiveAction = "UPS on mixer",
            capaOwnerUserId = await UserId(c, "Dev QA"), capaDueDate = D(DateTime.Today.AddDays(14)), version = major.GetProperty("version").GetInt32(),
        });
        Assert.Equal("CapaInProgress", major.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await qc.Try(HttpMethod.Post, $"/api/v1/mfg/deviations/{majorId}/close", new { password = Pw })).Status);
        major = await qa.Post($"/api/v1/mfg/deviations/{majorId}/close", new { password = Pw, note = "CAPA verified" });
        Assert.Equal("Closed by Dev QA (QA) — CAPA verified", major.GetProperty("closureSignature").GetString());

        Assert.Equal("Released", (await qa.Post($"/api/v1/mfg/batches/{pb}/release", new { password = Pw })).GetProperty("status").GetString()); // minor stays open, does not block
        Assert.Equal("Open", (await c.Get($"/api/v1/mfg/deviations?productionBatchId={pb}")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == minor.GetProperty("id").GetGuid()).GetProperty("status").GetString());
    }
}
