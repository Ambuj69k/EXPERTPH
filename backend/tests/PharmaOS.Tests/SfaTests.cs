using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Field force (MR) CRM: scope by territory, tour plan approval, DCR window/locking/idempotency, sample bag, coverage, anonymisation, purge.</summary>
[Collection("api")]
public class SfaTests(ApiFixture f)
{
    private static string D(DateTime d) => d.ToString("yyyy-MM-dd");
    private static string Today => D(DateTime.Today);

    private async Task<TenantClient> PlatformAsync()
    {
        var http = f.Factory.CreateClient();
        var res = await http.PostAsJsonAsync("/api/v1/platform/auth/login", new { email = "admin@pharmaos.local", password = "Dev-Only-Admin@123" }, ApiFixture.Json);
        await TenantClient.EnsureOk(res);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>(ApiFixture.Json);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return new TenantClient(http, f);
    }

    private async Task<Guid> SetPlan(TenantClient c, string plan)
    {
        var tid = (await c.Get("/api/v1/me")).GetProperty("tenantId").GetGuid();
        await (await PlatformAsync()).Put($"/api/v1/platform/tenants/{tid}/subscription", new { planCode = plan, status = "Active", migration = "Completed" });
        return tid;
    }

    private static async Task<Guid> UserId(TenantClient owner, string name) =>
        (await owner.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("displayName").GetString() == name).GetProperty("id").GetGuid();

    private static object Doctor(string name, Guid territory, string cls, int? version = null) => new
    {
        name, speciality = "Physician", qualification = "MBBS", clinic = name + " Clinic", area = "Market", phone = "9811111111",
        territoryId = territory, @class = cls, consentRecordedOn = Today, isActive = true, version,
    };

    private static object Call(Guid doctor, Guid? requestId = null, object[]? samples = null, Guid? joint = null) => new
    {
        clientRequestId = requestId ?? Guid.NewGuid(), doctorId = doctor, productsDetailed = "Sample Tab", feedback = "Will try", jointWorkUserId = joint,
        latitude = 19.07m, longitude = 72.87m, samples = samples ?? [],
    };

    [Fact]
    public async Task Field_force_end_to_end()
    {
        var c = await f.NewTenantAsync();
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, "/api/v1/sfa/territories"));
        await SetPlan(c, "fieldforce");
        var product = await c.CreateProductAsync("Sample Tab");

        var mgr = await c.AddUserAsync("AreaManager", "Meera Manager");
        var rep1 = await c.AddUserAsync("MedicalRep", "Rahul Rep");
        var rep2 = await c.AddUserAsync("MedicalRep", "Sonia Rep");
        var cashier = await c.AddUserAsync("Cashier");
        var (mgrId, rep1Id, rep2Id) = (await UserId(c, "Meera Manager"), await UserId(c, "Rahul Rep"), await UserId(c, "Sonia Rep"));
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.Try(HttpMethod.Get, "/api/v1/sfa/doctors")).Status);

        // territories (owner only)
        Assert.Equal(HttpStatusCode.Forbidden, (await rep1.Try(HttpMethod.Post, "/api/v1/sfa/territories", new { name = "X", isActive = true })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.Try(HttpMethod.Post, "/api/v1/sfa/territories", new { name = "Bad", repUserId = mgrId, isActive = true })).Status);
        var t1 = (await c.Post("/api/v1/sfa/territories", new { name = "Andheri", repUserId = rep1Id, managerUserId = mgrId, isActive = true })).GetProperty("id").GetGuid();
        var t2 = (await c.Post("/api/v1/sfa/territories", new { name = "Thane", repUserId = rep2Id, isActive = true })).GetProperty("id").GetGuid();

        // doctors: an MR adds only in their own territory
        var d1 = (await rep1.Post("/api/v1/sfa/doctors", Doctor("Dr Mehta", t1, "A"))).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.BadRequest, (await rep1.Try(HttpMethod.Post, "/api/v1/sfa/doctors", Doctor("Dr Elsewhere", t2, "B"))).Status);
        var d2 = (await c.Post("/api/v1/sfa/doctors", Doctor("Dr Thane", t2, "C"))).GetProperty("id").GetGuid();
        var d3 = (await mgr.Post("/api/v1/sfa/doctors", Doctor("Dr Shah", t1, "B"))).GetProperty("id").GetGuid();
        Assert.Equal(2, (await rep1.Get("/api/v1/sfa/doctors")).GetArrayLength());
        Assert.Equal("Dr Thane", Assert.Single((await rep2.Get("/api/v1/sfa/doctors")).EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(2, (await mgr.Get("/api/v1/sfa/doctors")).GetArrayLength());
        Assert.Equal(3, (await c.Get("/api/v1/sfa/doctors")).GetArrayLength());

        // tour plan: draft → submit → manager approval; locked afterwards
        var plan = await rep1.Get($"/api/v1/sfa/tour-plans/mine?month={Today}");
        var planId = plan.GetProperty("id").GetGuid();
        Assert.Equal("Draft", plan.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, (await rep1.Try(HttpMethod.Put, $"/api/v1/sfa/tour-plans/{planId}",
            new { version = plan.GetProperty("version").GetInt32(), entries = new[] { new { date = Today, doctorId = d2 } } })).Status);
        plan = await rep1.Put($"/api/v1/sfa/tour-plans/{planId}", new
        {
            version = plan.GetProperty("version").GetInt32(),
            entries = new[] { new { date = Today, doctorId = d1 }, new { date = Today, doctorId = d3 } },
        });
        Assert.Equal(2, plan.GetProperty("entries").GetArrayLength());
        await rep1.Post($"/api/v1/sfa/tour-plans/{planId}/submit", new { });
        Assert.Equal(HttpStatusCode.Forbidden, (await rep2.Try(HttpMethod.Post, $"/api/v1/sfa/tour-plans/{planId}/approve", new { })).Status);
        Assert.Equal(HttpStatusCode.BadRequest, (await mgr.Try(HttpMethod.Post, $"/api/v1/sfa/tour-plans/{planId}/reject", new { comment = "" })).Status);
        Assert.Equal("Approved", (await mgr.Post($"/api/v1/sfa/tour-plans/{planId}/approve", new { comment = "ok" })).GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "locked"), await rep1.Try(HttpMethod.Put, $"/api/v1/sfa/tour-plans/{planId}",
            new { version = 99, entries = Array.Empty<object>() }));

        // samples: only the MR's own manager (or owner) issues
        Assert.Equal(HttpStatusCode.Forbidden, (await rep1.Try(HttpMethod.Post, "/api/v1/sfa/samples/issue", new { repUserId = rep1Id, productId = product, qty = 10 })).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.Try(HttpMethod.Post, "/api/v1/sfa/samples/issue", new { repUserId = rep2Id, productId = product, qty = 10 })).Status);
        await mgr.Post("/api/v1/sfa/samples/issue", new { repUserId = rep1Id, productId = product, qty = 10, note = "Monthly bag" });

        // daily call report: reporting window, planned visits, idempotent saves, sample bag
        Assert.Equal((HttpStatusCode.Conflict, "future_date"), await rep1.Try(HttpMethod.Post, "/api/v1/sfa/reports/open", new { date = D(DateTime.Today.AddDays(1)) }));
        Assert.Equal((HttpStatusCode.Conflict, "outside_window"), await rep1.Try(HttpMethod.Post, "/api/v1/sfa/reports/open", new { date = D(DateTime.Today.AddDays(-8)) }));
        var day = await rep1.Post("/api/v1/sfa/reports/open", new { date = Today });
        var dayId = day.GetProperty("id").GetGuid();
        Assert.Equal(2, day.GetProperty("planned").GetArrayLength());

        var req = Guid.NewGuid();
        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/calls", Call(d1, req, [new { productId = product, qty = 3 }], mgrId));
        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/calls", Call(d1, req, [new { productId = product, qty = 3 }], mgrId)); // retried
        var call1 = Assert.Single(day.GetProperty("calls").EnumerateArray());
        Assert.True(call1.GetProperty("wasPlanned").GetBoolean());
        Assert.True(call1.GetProperty("hasLocation").GetBoolean());
        Assert.Equal("Meera Manager", call1.GetProperty("jointWorkWith").GetString());
        Assert.Equal(3m, Assert.Single(call1.GetProperty("samples").EnumerateArray()).GetProperty("qty").GetDecimal());
        Assert.Equal((HttpStatusCode.Conflict, "duplicate_call"), await rep1.Try(HttpMethod.Post, $"/api/v1/sfa/reports/{dayId}/calls", Call(d1)));
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_samples"), await rep1.Try(HttpMethod.Post, $"/api/v1/sfa/reports/{dayId}/calls",
            Call(d3, samples: [new { productId = product, qty = 20 }])));
        Assert.Equal(HttpStatusCode.NotFound, (await rep1.Try(HttpMethod.Post, $"/api/v1/sfa/reports/{dayId}/calls", Call(d2))).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await rep2.Try(HttpMethod.Get, $"/api/v1/sfa/reports/{dayId}")).Status);

        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/calls", Call(d3, samples: [new { productId = product, qty = 2 }]));
        Assert.Equal(5m, (await rep1.Get("/api/v1/sfa/samples")).EnumerateArray().Single().GetProperty("balance").GetDecimal());
        var call3 = day.GetProperty("calls").EnumerateArray().Single(x => x.GetProperty("doctorId").GetGuid() == d3).GetProperty("id").GetGuid();
        day = await rep1.Delete($"/api/v1/sfa/reports/{dayId}/calls/{call3}");
        Assert.Equal(7m, (await rep1.Get("/api/v1/sfa/samples")).EnumerateArray().Single().GetProperty("balance").GetDecimal()); // reversed
        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/calls", Call(d3));

        // submit → locked → sent back → fixed → approved
        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/submit", new { notes = "Good day", version = day.GetProperty("version").GetInt32() });
        Assert.Equal("Submitted", day.GetProperty("status").GetString());
        Assert.Equal((HttpStatusCode.Conflict, "locked"), await rep1.Try(HttpMethod.Post, $"/api/v1/sfa/reports/{dayId}/calls", Call(d1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await rep1.Try(HttpMethod.Post, $"/api/v1/sfa/reports/{dayId}/approve", new { })).Status);
        await mgr.Post($"/api/v1/sfa/reports/{dayId}/reject", new { comment = "Add feedback for Dr Shah" });
        day = await rep1.Get($"/api/v1/sfa/reports/{dayId}");
        Assert.Equal("Rejected", day.GetProperty("status").GetString());
        day = await rep1.Post($"/api/v1/sfa/reports/{dayId}/submit", new { notes = "Feedback added", version = day.GetProperty("version").GetInt32() });
        Assert.Equal("Approved", (await mgr.Post($"/api/v1/sfa/reports/{dayId}/approve", new { })).GetProperty("status").GetString());

        // coverage: A needs 4, B needs 2 → both still short; both planned visits done
        var cov = (await mgr.Get($"/api/v1/sfa/coverage?month={Today}")).EnumerateArray().Single(x => x.GetProperty("repUserId").GetGuid() == rep1Id);
        Assert.Equal(2, cov.GetProperty("doctors").GetInt32());
        Assert.Equal(6, cov.GetProperty("requiredVisits").GetInt32());
        Assert.Equal(2, cov.GetProperty("visits").GetInt32());
        Assert.Equal(0, cov.GetProperty("doctorsMet").GetInt32());
        Assert.Equal(2, cov.GetProperty("planned").GetInt32());
        Assert.Equal(2, cov.GetProperty("plannedMet").GetInt32());
        Assert.Equal(2, cov.GetProperty("missed").GetArrayLength());
        Assert.Single((await mgr.Get($"/api/v1/sfa/coverage?month={Today}")).EnumerateArray()); // only their own MR

        var bag = (await mgr.Get($"/api/v1/sfa/samples?repUserId={rep1Id}")).EnumerateArray().Single();
        Assert.Equal((10m, 3m, 7m), (bag.GetProperty("issued").GetDecimal(), bag.GetProperty("given").GetDecimal(), bag.GetProperty("balance").GetDecimal()));
        await mgr.Post("/api/v1/sfa/samples/return", new { repUserId = rep1Id, productId = product, qty = 7 });
        Assert.Equal((HttpStatusCode.Conflict, "insufficient_samples"), await mgr.Try(HttpMethod.Post, "/api/v1/sfa/samples/return", new { repUserId = rep1Id, productId = product, qty = 1 }));

        // anonymise on request (owner only); visits stay countable
        Assert.Equal(HttpStatusCode.Forbidden, (await mgr.Try(HttpMethod.Post, $"/api/v1/sfa/doctors/{d1}/anonymise", new { })).Status);
        await c.Post($"/api/v1/sfa/doctors/{d1}/anonymise", new { });
        var gone = (await c.Get("/api/v1/sfa/doctors?includeInactive=true")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == d1);
        Assert.Equal("Removed doctor", gone.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, gone.GetProperty("phone").ValueKind);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Try(HttpMethod.Get, $"/api/v1/sfa/reports/{Guid.NewGuid()}")).Status);
    }

    [Fact]
    public async Task Purge_covers_distributor_and_field_force_data()
    {
        var c = await f.NewTenantAsync();
        var tid = await SetPlan(c, "enterprise");
        var product = await c.CreateProductAsync("Purge Mix");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, product, ("PM1", 12, 10, 100, 70));

        var retailer = (await c.Post("/api/v1/distribution/retailers", new { name = "Purge Chemist", stateCode = "27", creditLimit = 0, creditDays = 30, isActive = true }))
            .GetProperty("id").GetGuid();
        await c.Put("/api/v1/distribution/rates", new { productId = product, retailerId = (Guid?)null, ratePerPack = 80 });
        await c.Post("/api/v1/distribution/invoices", new { clientRequestId = Guid.NewGuid(), retailerId = retailer, lines = new[] { new { productId = product, qtyPacks = 2 } } });

        var rep = await c.AddUserAsync("MedicalRep", "Purge Rep");
        var repId = await UserId(c, "Purge Rep");
        var t = (await c.Post("/api/v1/sfa/territories", new { name = "Purge T", repUserId = repId, isActive = true })).GetProperty("id").GetGuid();
        var doc = (await rep.Post("/api/v1/sfa/doctors", Doctor("Dr Purge", t, "A"))).GetProperty("id").GetGuid();
        await c.Post("/api/v1/sfa/samples/issue", new { repUserId = repId, productId = product, qty = 5 });
        var day = await rep.Post("/api/v1/sfa/reports/open", new { date = Today });
        await rep.Post($"/api/v1/sfa/reports/{day.GetProperty("id").GetGuid()}/calls", Call(doc, samples: [new { productId = product, qty = 1 }]));
        await rep.Get($"/api/v1/sfa/tour-plans/mine?month={Today}");

        var dept = (await c.Post("/api/v1/hospital/departments", new { name = "Purge Ward", code = "PW", isActive = true })).GetProperty("id").GetGuid();
        var nurse = await c.AddUserAsync("WardStaff", "Purge Nurse");
        await c.Put($"/api/v1/hospital/staff/{await UserId(c, "Purge Nurse")}", new { departmentId = dept });
        await c.Put("/api/v1/hospital/formulary", new { productId = product, inFormulary = true, isHighAlert = true });
        var ind = await nurse.Post("/api/v1/hospital/indents", new { departmentId = dept, priority = "Routine", lines = new[] { new { productId = product, qtyUnits = 4m } } });
        await c.Post("/api/v1/hospital/issues", new
        {
            departmentId = dept, wardIndentId = ind.GetProperty("id").GetGuid(),
            lines = new[] { new { productId = product, qtyUnits = 4m, wardIndentLineId = ind.GetProperty("lines")[0].GetProperty("id").GetGuid() } },
        }); // stays pending (high-alert) — still must be purged

        var fg = await c.CreateProductAsync("Purge FG");
        var recipe = await c.Post("/api/v1/mfg/recipes", new { productId = fg, batchSizeUnits = 10, lines = new[] { new { materialProductId = product, qtyUnits = 2m } } });
        await c.Post($"/api/v1/mfg/recipes/{recipe.GetProperty("id").GetGuid()}/approve", new { password = "Password@123" });
        var pb = await c.Post("/api/v1/mfg/batches", new { recipeId = recipe.GetProperty("id").GetGuid(), batchNo = "PG-1", expiryDate = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd"), mrp = 50, plannedUnits = 10 });
        await c.Post($"/api/v1/mfg/batches/{pb.GetProperty("id").GetGuid()}/issue-materials", new { });
        await c.Post($"/api/v1/mfg/batches/{pb.GetProperty("id").GetGuid()}/output", new { outputUnits = 10 });

        var platform = await PlatformAsync();
        await platform.Post($"/api/v1/platform/tenants/{tid}/schedule-deletion", new { daysFromNow = 0 });
        await platform.Post($"/api/v1/platform/tenants/{tid}/purge", new { });
        var row = (await platform.Get("/api/v1/platform/tenants")).EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == tid);
        Assert.Equal("Deleted tenant", row.GetProperty("name").GetString());
    }
}
