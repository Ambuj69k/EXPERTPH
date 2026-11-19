using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

/// <summary>Field force (MR) CRM endpoints. Row-level scope (own / supervised / all) is enforced in <see cref="SfaService"/>.</summary>
public static class SfaEndpoints
{
    public static void MapSfaEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/sfa").RequireAuthorization(Policies.Tenant).RequireAuthorization(Policies.Sfa).RequireModule(Plans.Sfa);

        g.MapGet("/team", (SfaService s, CancellationToken ct) => s.TeamAsync(ct));
        g.MapGet("/territories", (SfaService s, CancellationToken ct) => s.TerritoriesAsync(ct));
        g.MapPost("/territories", (TerritoryInput r, SfaService s, CancellationToken ct) => s.SaveTerritoryAsync(null, r, ct)).RequireAuthorization(Policies.Admin);
        g.MapPut("/territories/{id:guid}", (Guid id, TerritoryInput r, SfaService s, CancellationToken ct) => s.SaveTerritoryAsync(id, r, ct)).RequireAuthorization(Policies.Admin);

        g.MapGet("/doctors", (string? q, Guid? territoryId, bool? includeInactive, SfaService s, CancellationToken ct) => s.DoctorsAsync(q, territoryId, includeInactive ?? false, ct));
        g.MapPost("/doctors", (DoctorInput r, SfaService s, CancellationToken ct) => s.SaveDoctorAsync(null, r, ct));
        g.MapPut("/doctors/{id:guid}", (Guid id, DoctorInput r, SfaService s, CancellationToken ct) => s.SaveDoctorAsync(id, r, ct));
        g.MapPost("/doctors/{id:guid}/anonymise", async (Guid id, SfaService s, CancellationToken ct) => { await s.AnonymiseDoctorAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Policies.Admin);

        g.MapGet("/tour-plans/mine", (DateOnly month, SfaService s, CancellationToken ct) => s.MyTourPlanAsync(month, ct));
        g.MapGet("/tour-plans", (DateOnly month, SfaService s, CancellationToken ct) => s.TourPlansAsync(month, ct));
        g.MapGet("/tour-plans/{id:guid}", (Guid id, SfaService s, CancellationToken ct) => s.GetTourPlanAsync(id, ct));
        g.MapPut("/tour-plans/{id:guid}", (Guid id, TourPlanInput r, SfaService s, CancellationToken ct) => s.SaveTourPlanAsync(id, r, ct));
        g.MapPost("/tour-plans/{id:guid}/submit", (Guid id, SfaService s, CancellationToken ct) => s.SubmitTourPlanAsync(id, ct));
        g.MapPost("/tour-plans/{id:guid}/approve", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewTourPlanAsync(id, true, r.Comment, ct))
            .RequireAuthorization(Policies.SfaManage);
        g.MapPost("/tour-plans/{id:guid}/reject", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewTourPlanAsync(id, false, r.Comment, ct))
            .RequireAuthorization(Policies.SfaManage);

        g.MapPost("/reports/open", (OpenDayRequest r, SfaService s, CancellationToken ct) => s.OpenDayAsync(r.Date, ct));
        g.MapGet("/reports", (DateOnly from, DateOnly to, Guid? repUserId, SfaStatus? status, SfaService s, CancellationToken ct) => s.ReportsAsync(from, to, repUserId, status, ct));
        g.MapGet("/reports/{id:guid}", (Guid id, SfaService s, CancellationToken ct) => s.GetReportAsync(id, ct));
        g.MapPost("/reports/{id:guid}/calls", (Guid id, CallInput r, SfaService s, CancellationToken ct) => s.AddCallAsync(id, r, ct));
        g.MapDelete("/reports/{id:guid}/calls/{callId:guid}", (Guid id, Guid callId, SfaService s, CancellationToken ct) => s.RemoveCallAsync(id, callId, ct));
        g.MapPost("/reports/{id:guid}/submit", (Guid id, SubmitReportInput r, SfaService s, CancellationToken ct) => s.SubmitReportAsync(id, r, ct));
        g.MapPost("/reports/{id:guid}/approve", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewReportAsync(id, true, r.Comment, ct))
            .RequireAuthorization(Policies.SfaManage);
        g.MapPost("/reports/{id:guid}/reject", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewReportAsync(id, false, r.Comment, ct))
            .RequireAuthorization(Policies.SfaManage);

        g.MapGet("/samples", (Guid? repUserId, SfaService s, CancellationToken ct) => s.SampleBalancesAsync(repUserId, ct));
        g.MapGet("/samples/ledger", (Guid repUserId, SfaService s, CancellationToken ct) => s.SampleLedgerAsync(repUserId, ct));
        g.MapPost("/samples/issue", async (SampleIssueInput r, SfaService s, CancellationToken ct) => { await s.IssueSamplesAsync(r, false, ct); return Results.NoContent(); })
            .RequireAuthorization(Policies.SfaManage);
        g.MapPost("/samples/return", async (SampleIssueInput r, SfaService s, CancellationToken ct) => { await s.IssueSamplesAsync(r, true, ct); return Results.NoContent(); })
            .RequireAuthorization(Policies.SfaManage);

        g.MapGet("/expenses", (DateOnly month, SfaStatus? status, SfaService s, CancellationToken ct) => s.ExpensesAsync(month, status, ct));
        g.MapPost("/expenses", (ExpenseInput r, SfaService s, CancellationToken ct) => s.ClaimExpenseAsync(r, ct));
        g.MapPost("/expenses/{id:guid}/approve", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewExpenseAsync(id, true, r.Comment, ct)).RequireAuthorization(Policies.SfaManage);
        g.MapPost("/expenses/{id:guid}/reject", (Guid id, ReviewInput r, SfaService s, CancellationToken ct) => s.ReviewExpenseAsync(id, false, r.Comment, ct)).RequireAuthorization(Policies.SfaManage);
        g.MapPut("/targets", async (TargetInput r, SfaService s, CancellationToken ct) => { await s.SetTargetAsync(r, ct); return Results.NoContent(); }).RequireAuthorization(Policies.SfaManage);
        g.MapGet("/scorecard", (DateOnly month, SfaService s, CancellationToken ct) => s.ScorecardAsync(month, ct));
        g.MapGet("/coverage", (DateOnly month, SfaService s, CancellationToken ct) => s.CoverageAsync(month, ct));
    }
}
