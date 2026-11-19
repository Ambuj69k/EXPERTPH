using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

/// <summary>Manufacturing &amp; QA endpoints (pilot; not GMP-validated). Duties are split by role and enforced again in the service.</summary>
public static class ManufacturingEndpoints
{
    public static void MapManufacturingEndpoints(this WebApplication app)
    {
        var g = app.MapGroup("/api/v1/mfg").RequireAuthorization(Policies.Tenant).RequireAuthorization(Policies.Mfg).RequireModule(Plans.Manufacturing);

        g.MapGet("/recipes", (ManufacturingService s, CancellationToken ct) => s.RecipesAsync(ct));
        g.MapPost("/recipes", (RecipeInput r, ManufacturingService s, CancellationToken ct) => s.CreateRecipeAsync(r, ct)).RequireAuthorization(Policies.MfgProduction);
        g.MapPost("/recipes/{id:guid}/approve", (Guid id, SignatureInput r, ManufacturingService s, CancellationToken ct) => s.ApproveRecipeAsync(id, r, ct)).RequireAuthorization(Policies.MfgQa);

        g.MapGet("/specs", (ManufacturingService s, CancellationToken ct) => s.SpecsAsync(ct));
        g.MapPost("/specs", (SpecInput r, ManufacturingService s, CancellationToken ct) => s.CreateSpecAsync(r, ct)).RequireAuthorization(Policies.MfgQc);
        g.MapPost("/specs/{id:guid}/approve", (Guid id, SignatureInput r, ManufacturingService s, CancellationToken ct) => s.ApproveSpecAsync(id, r, ct)).RequireAuthorization(Policies.MfgQa);

        g.MapGet("/samples", (QcSampleStatus? status, ManufacturingService s, CancellationToken ct) => s.SamplesAsync(status, ct));
        g.MapPost("/samples", (QcSampleInput r, ManufacturingService s, CancellationToken ct) => s.CreateSampleAsync(r, ct)).RequireAuthorization(Policies.MfgQc);
        g.MapPost("/samples/{id:guid}/results", (Guid id, QcResultsInput r, ManufacturingService s, CancellationToken ct) => s.RecordResultsAsync(id, r, ct)).RequireAuthorization(Policies.MfgQc);

        g.MapGet("/batches", (ProductionStatus? status, ManufacturingService s, CancellationToken ct) => s.BatchesAsync(status, ct));
        g.MapGet("/batches/{id:guid}", (Guid id, ManufacturingService s, CancellationToken ct) => s.GetBatchAsync(id, ct));
        g.MapPost("/batches", (ProductionInput r, ManufacturingService s, CancellationToken ct) => s.PlanBatchAsync(r, ct)).RequireAuthorization(Policies.MfgProduction);
        g.MapPost("/batches/{id:guid}/issue-materials", (Guid id, ManufacturingService s, CancellationToken ct) => s.IssueMaterialsAsync(id, ct)).RequireAuthorization(Policies.MfgProduction);
        g.MapPost("/batches/{id:guid}/output", (Guid id, OutputInput r, ManufacturingService s, CancellationToken ct) => s.RecordOutputAsync(id, r, ct)).RequireAuthorization(Policies.MfgProduction);
        g.MapPost("/batches/{id:guid}/cancel", (Guid id, ManufacturingService s, CancellationToken ct) => s.CancelBatchAsync(id, ct)).RequireAuthorization(Policies.MfgProduction);
        g.MapPost("/batches/{id:guid}/release", (Guid id, SignatureInput r, ManufacturingService s, CancellationToken ct) => s.DecideAsync(id, true, r, ct)).RequireAuthorization(Policies.MfgQa);
        g.MapPost("/batches/{id:guid}/reject", (Guid id, SignatureInput r, ManufacturingService s, CancellationToken ct) => s.DecideAsync(id, false, r, ct)).RequireAuthorization(Policies.MfgQa);

        g.MapGet("/deviations", (DeviationStatus? status, Guid? productionBatchId, ManufacturingService s, CancellationToken ct) => s.DeviationsAsync(status, productionBatchId, ct));
        g.MapPost("/deviations", (DeviationInput r, ManufacturingService s, CancellationToken ct) => s.RaiseDeviationAsync(r, ct));
        g.MapPost("/deviations/{id:guid}/investigation", (Guid id, InvestigationInput r, ManufacturingService s, CancellationToken ct) => s.InvestigateAsync(id, r, ct));
        g.MapPost("/deviations/{id:guid}/close", (Guid id, SignatureInput r, ManufacturingService s, CancellationToken ct) => s.CloseDeviationAsync(id, r, ct)).RequireAuthorization(Policies.MfgQa);

        g.MapGet("/lots", (string q, ManufacturingService s, CancellationToken ct) => s.FindBatchesAsync(q, ct));
        g.MapGet("/genealogy/{batchId:guid}", (Guid batchId, ManufacturingService s, CancellationToken ct) => s.GenealogyAsync(batchId, ct));
    }
}
