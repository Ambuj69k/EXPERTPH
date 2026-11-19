using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

public record IssueVerifyRequest(int Version);

/// <summary>Hospital pharmacy endpoints. Ward staff are confined to their own department inside <see cref="HospitalService"/>.</summary>
public static class HospitalEndpoints
{
    public static void MapHospitalEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/hospital").RequireAuthorization(Policies.Tenant).RequireAuthorization(Policies.Hospital).RequireModule(Plans.Hospital);
        const string pharmacy = Policies.HospitalPharmacy;

        g.MapGet("/departments", (HospitalService s, CancellationToken ct) => s.DepartmentsAsync(ct));
        g.MapPost("/departments", (DepartmentInput r, HospitalService s, CancellationToken ct) => s.SaveDepartmentAsync(null, r, ct)).RequireAuthorization(pharmacy);
        g.MapPut("/departments/{id:guid}", (Guid id, DepartmentInput r, HospitalService s, CancellationToken ct) => s.SaveDepartmentAsync(id, r, ct)).RequireAuthorization(pharmacy);
        g.MapPut("/staff/{userId:guid}", async (Guid userId, StaffDepartmentInput r, HospitalService s, CancellationToken ct) =>
        { await s.SetStaffDepartmentAsync(userId, r.DepartmentId, ct); return Results.NoContent(); }).RequireAuthorization(Policies.Admin);

        g.MapGet("/formulary", (string? q, HospitalService s, CancellationToken ct) => s.FormularyAsync(q, ct));
        g.MapPut("/formulary", async (FormularyInput r, HospitalService s, CancellationToken ct) => { await s.SetFormularyAsync(r, ct); return Results.NoContent(); })
            .RequireAuthorization(pharmacy);

        g.MapGet("/indents", (WardIndentStatus? status, Guid? departmentId, HospitalService s, CancellationToken ct) => s.IndentsAsync(status, departmentId, ct));
        g.MapPost("/indents", (WardIndentInput r, HospitalService s, CancellationToken ct) => s.CreateIndentAsync(r, ct));
        g.MapGet("/indents/{id:guid}", (Guid id, HospitalService s, CancellationToken ct) => s.GetIndentAsync(id, ct));
        g.MapPost("/indents/{id:guid}/cancel", (Guid id, ReasonRequest r, HospitalService s, CancellationToken ct) => s.CancelIndentAsync(id, r.Reason, ct));

        g.MapGet("/issues", (WardIssueStatus? status, Guid? departmentId, HospitalService s, CancellationToken ct) => s.IssuesAsync(status, departmentId, ct));
        g.MapGet("/issues/{id:guid}", (Guid id, HospitalService s, CancellationToken ct) => s.GetIssueAsync(id, ct));
        g.MapPost("/issues", (WardIssueInput r, HospitalService s, CancellationToken ct) => s.PrepareIssueAsync(r, ct)).RequireAuthorization(pharmacy);
        g.MapPost("/issues/{id:guid}/verify", (Guid id, IssueVerifyRequest r, HospitalService s, CancellationToken ct) => s.VerifyIssueAsync(id, r.Version, ct)).RequireAuthorization(pharmacy);
        g.MapPost("/issues/{id:guid}/cancel", (Guid id, ReasonRequest r, HospitalService s, CancellationToken ct) => s.CancelIssueAsync(id, r.Reason, ct)).RequireAuthorization(pharmacy);

        g.MapGet("/returns", (HospitalService s, CancellationToken ct) => s.ReturnsAsync(ct));
        g.MapPost("/returns", (WardReturnInput r, HospitalService s, CancellationToken ct) => s.CreateReturnAsync(r, ct)).RequireAuthorization(pharmacy);

        g.MapGet("/reports/consumption", (DateOnly from, DateOnly to, HospitalService s, CancellationToken ct) => s.ConsumptionAsync(from, to, ct)).RequireAuthorization(pharmacy);
    }
}
