using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace PharmaOS.Tests;

/// <summary>Hospital pharmacy: departments, formulary, ward indents, FEFO issues, second check for high-alert/narcotics, returns, consumption, books.</summary>
[Collection("api")]
public class HospitalTests(ApiFixture f)
{
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

    [Fact]
    public async Task Hospital_pharmacy_end_to_end()
    {
        var c = await f.NewTenantAsync();
        Assert.Equal((HttpStatusCode.Forbidden, "module_not_in_plan"), await c.Try(HttpMethod.Get, "/api/v1/hospital/departments"));
        await SetPlan(c, "hospital");

        var para = await c.CreateProductAsync("Para Hosp");
        var insulin = await c.CreateProductAsync("Insulin Hosp");
        var morphine = await c.CreateProductAsync("Morphine Hosp", schedule: "NarcoticPsychotropic");
        var supplier = await c.CreateSupplierAsync();
        await c.PurchaseAsync(supplier, para, ("P-LATE", 24, 5, 100, 70), ("P-SOON", 6, 2, 100, 50)); // 50 + 20 tabs
        await c.PurchaseAsync(supplier, insulin, ("I1", 12, 3, 100, 85));
        await c.PurchaseAsync(supplier, morphine, ("M1", 12, 1, 100, 85));

        var icu = (await c.Post("/api/v1/hospital/departments", new { name = "ICU", code = "icu", isActive = true })).GetProperty("id").GetGuid();
        var ward = (await c.Post("/api/v1/hospital/departments", new { name = "General Ward 1", code = "GW1", isActive = true })).GetProperty("id").GetGuid();
        Assert.Equal((HttpStatusCode.Conflict, "duplicate"), await c.Try(HttpMethod.Post, "/api/v1/hospital/departments", new { name = "X", code = "ICU", isActive = true }));

        var nurse = await c.AddUserAsync("WardStaff", "Nurse Asha");
        var pharm2 = await c.AddUserAsync("Pharmacist", "Pharm Two");
        var nurseId = (await c.Get("/api/v1/users")).EnumerateArray().Single(u => u.GetProperty("displayName").GetString() == "Nurse Asha").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Get, "/api/v1/hospital/indents")).Status); // not assigned yet
        await c.Put($"/api/v1/hospital/staff/{nurseId}", new { departmentId = ward });
        Assert.Equal("General Ward 1", Assert.Single((await nurse.Get("/api/v1/hospital/departments")).EnumerateArray()).GetProperty("name").GetString());

        // formulary: only formulary items can be indented; insulin is high-alert
        await c.Put("/api/v1/hospital/formulary", new { productId = para, inFormulary = true, isHighAlert = false });
        await c.Put("/api/v1/hospital/formulary", new { productId = insulin, inFormulary = true, isHighAlert = true, notes = "Double-check units" });
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Put, "/api/v1/hospital/formulary", new { productId = morphine, inFormulary = true, isHighAlert = false })).Status);
        object Indent(Guid dept, params (Guid P, decimal Q)[] lines) => new { departmentId = dept, priority = "Urgent", encounterRef = "IP-2026-001", lines = lines.Select(l => new { productId = l.P, qtyUnits = l.Q }) };
        Assert.Equal(HttpStatusCode.BadRequest, (await nurse.Try(HttpMethod.Post, "/api/v1/hospital/indents", Indent(ward, (morphine, 5)))).Status);
        await c.Put("/api/v1/hospital/formulary", new { productId = morphine, inFormulary = true, isHighAlert = false });
        var formulary = await c.Get("/api/v1/hospital/formulary");
        Assert.True(formulary.EnumerateArray().Single(x => x.GetProperty("productId").GetGuid() == morphine).GetProperty("needsSecondCheck").GetBoolean());

        Assert.Equal(HttpStatusCode.NotFound, (await nurse.Try(HttpMethod.Post, "/api/v1/hospital/indents", Indent(icu, (para, 5)))).Status); // not their department
        var indent = await nurse.Post("/api/v1/hospital/indents", Indent(ward, (para, 30), (insulin, 20), (morphine, 5)));
        var indentId = indent.GetProperty("id").GetGuid();
        Guid Line(JsonElement ind, Guid p) => ind.GetProperty("lines").EnumerateArray().Single(l => l.GetProperty("productId").GetGuid() == p).GetProperty("id").GetGuid();
        object Issue(params (Guid P, decimal Q)[] lines) => new
        {
            departmentId = ward, wardIndentId = indentId, receivedBy = "Sr Nurse",
            lines = lines.Select(l => new { productId = l.P, qtyUnits = l.Q, wardIndentLineId = Line(indent, l.P) }),
        };

        // ordinary item: issued at once, FEFO (soon-expiring batch first)
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Post, "/api/v1/hospital/issues", Issue((para, 20)))).Status);
        var issue1 = await c.Post("/api/v1/hospital/issues", Issue((para, 20)));
        Assert.Equal("Issued", issue1.GetProperty("status").GetString());
        var b1 = Assert.Single(Assert.Single(issue1.GetProperty("lines").EnumerateArray()).GetProperty("batches").EnumerateArray());
        Assert.Equal("P-SOON", b1.GetProperty("batchNo").GetString());
        Assert.Equal(50m, await c.StockUnitsAsync(para));
        Assert.Equal("PartlyIssued", (await nurse.Get($"/api/v1/hospital/indents/{indentId}")).GetProperty("status").GetString());

        // high-alert + narcotic: prepared, then a different pharmacist verifies; stock moves only then
        var issue2 = await c.Post("/api/v1/hospital/issues", Issue((insulin, 20), (morphine, 5)));
        var issue2Id = issue2.GetProperty("id").GetGuid();
        Assert.Equal("PendingCheck", issue2.GetProperty("status").GetString());
        Assert.Equal(30m, await c.StockUnitsAsync(insulin));
        Assert.Equal((HttpStatusCode.Conflict, "same_person"), await c.Try(HttpMethod.Post, $"/api/v1/hospital/issues/{issue2Id}/verify", new { version = issue2.GetProperty("version").GetInt32() }));
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Post, $"/api/v1/hospital/issues/{issue2Id}/verify", new { version = 0 })).Status);
        Assert.Equal((HttpStatusCode.Conflict, "issue_pending"), await nurse.Try(HttpMethod.Post, $"/api/v1/hospital/indents/{indentId}/cancel", new { reason = "not needed" }));
        issue2 = await pharm2.Post($"/api/v1/hospital/issues/{issue2Id}/verify", new { version = issue2.GetProperty("version").GetInt32() });
        Assert.Equal("Issued", issue2.GetProperty("status").GetString());
        Assert.Equal("Pharm Two", issue2.GetProperty("checkedBy").GetString());
        Assert.Equal(10m, await c.StockUnitsAsync(insulin));
        Assert.Equal(5m, await c.StockUnitsAsync(morphine));

        // cannot issue more than the indent still needs; finishing it closes the indent
        Assert.Equal((HttpStatusCode.Conflict, "over_issue"), await c.Try(HttpMethod.Post, "/api/v1/hospital/issues", Issue((para, 15))));
        var issue3 = await pharm2.Post("/api/v1/hospital/issues", Issue((para, 10)));
        Assert.Equal("P-LATE", issue3.GetProperty("lines")[0].GetProperty("batches")[0].GetProperty("batchNo").GetString());
        Assert.Equal("Closed", (await c.Get($"/api/v1/hospital/indents/{indentId}")).GetProperty("status").GetString());

        // returns: unused goods back to stock (pharmacy receives them); not more than was issued
        var ret = new { wardIssueId = issue1.GetProperty("id").GetGuid(), reason = "Unused", lines = new[] { new { wardIssueBatchId = b1.GetProperty("id").GetGuid(), qtyUnits = 5m } } };
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Post, "/api/v1/hospital/returns", ret)).Status);
        await c.Post("/api/v1/hospital/returns", ret);
        Assert.Equal(45m, await c.StockUnitsAsync(para));
        Assert.Equal((HttpStatusCode.Conflict, "over_return"), await c.Try(HttpMethod.Post, "/api/v1/hospital/returns",
            new { wardIssueId = issue1.GetProperty("id").GetGuid(), reason = "Damaged", lines = new[] { new { wardIssueBatchId = b1.GetProperty("id").GetGuid(), qtyUnits = 16m } } }));

        // ward staff see only their department's issues
        Assert.Equal(3, (await nurse.Get("/api/v1/hospital/issues")).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await nurse.Try(HttpMethod.Get, $"/api/v1/hospital/reports/consumption?from={Today}&to={Today}")).Status);

        var rep = await c.Get($"/api/v1/hospital/reports/consumption?from={Today}&to={Today}");
        var gw = Assert.Single(rep.GetProperty("departments").EnumerateArray());
        var issued = issue1.GetProperty("costValue").GetDecimal() + issue2.GetProperty("costValue").GetDecimal() + issue3.GetProperty("costValue").GetDecimal();
        Assert.Equal(3, gw.GetProperty("issues").GetInt32());
        Assert.Equal(issued, gw.GetProperty("issuedValue").GetDecimal());
        Assert.Equal(issued - gw.GetProperty("returnedValue").GetDecimal(), gw.GetProperty("netValue").GetDecimal());
        Assert.True(gw.GetProperty("returnedValue").GetDecimal() > 0);
        Assert.Equal(25m, rep.GetProperty("topProducts").EnumerateArray().Single(p => p.GetProperty("productId").GetGuid() == para).GetProperty("units").GetDecimal());

        var tb = await c.Get($"/api/v1/accounting/trial-balance?asOf={Today}");
        Assert.True(tb.GetProperty("balanced").GetBoolean());
        foreach (var chk in (await c.Get("/api/v1/accounting/control-checks")).EnumerateArray())
            Assert.True(chk.GetProperty("difference").GetDecimal() == 0, $"{chk.GetProperty("name").GetString()} off by {chk.GetProperty("difference")}");
        await DbAssert.LedgerMatchesBalancesAsync(f);
    }
}
