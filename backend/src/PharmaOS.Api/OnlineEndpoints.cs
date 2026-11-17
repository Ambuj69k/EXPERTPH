using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

/// <summary>Online ordering: anonymous customer endpoints + store/delivery endpoints (module "online").</summary>
public static class OnlineEndpoints
{
    public static void MapOnlineEndpoints(this WebApplication app)
    {
        // ---- public (customers, no login) ----
        var pub = app.MapGroup("/api/v1/public").RequireRateLimiting("public").DisableAntiforgery();
        pub.MapGet("/stores/{branchId:guid}", (Guid branchId, PublicStoreService svc, CancellationToken ct) => svc.StoreAsync(branchId, ct));
        pub.MapGet("/stores/{branchId:guid}/catalog", (Guid branchId, string? q, PublicStoreService svc, CancellationToken ct) => svc.CatalogAsync(branchId, q, ct));
        pub.MapPost("/stores/{branchId:guid}/orders", (Guid branchId, PlaceOrderRequest r, PublicStoreService svc, CancellationToken ct) => svc.PlaceOrderAsync(branchId, r, ct));
        pub.MapGet("/orders/{token}", (string token, PublicStoreService svc, CancellationToken ct) => svc.TrackAsync(token, ct));
        pub.MapPost("/orders/{token}/approve", (string token, PublicStoreService svc, CancellationToken ct) => svc.CustomerDecisionAsync(token, true, ct));
        pub.MapPost("/orders/{token}/cancel", (string token, PublicStoreService svc, CancellationToken ct) => svc.CustomerDecisionAsync(token, false, ct));
        pub.MapPost("/orders/{token}/prescriptions", async (string token, HttpRequest req, PublicStoreService svc, CancellationToken ct) =>
        {
            if (!req.HasFormContentType) return Results.BadRequest();
            var form = await req.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null) return Results.BadRequest();
            await using var s = file.OpenReadStream();
            return Results.Ok(await svc.UploadPrescriptionAsync(token, s, file.FileName, ct));
        });

        // ---- store staff ----
        var staff = app.MapGroup("/api/v1/online").RequireAuthorization(Policies.Tenant).RequireModule(Plans.Online);
        staff.MapGet("/orders", (OnlineOrderStatus? status, OnlineOrderService svc, CancellationToken ct) => svc.ListAsync(status, ct)).RequireAuthorization(Policies.Sell);
        staff.MapGet("/orders/{id:guid}", (Guid id, OnlineOrderService svc, CancellationToken ct) => svc.GetAsync(id, ct)).RequireAuthorization(Policies.Delivery);
        staff.MapPost("/orders/{id:guid}/review", (Guid id, ReviewRequest r, OnlineOrderService svc, CancellationToken ct) => svc.ReviewAsync(id, r, ct)).RequireAuthorization(Policies.SalesAdmin);
        staff.MapPost("/orders/{id:guid}/pack", (Guid id, OnlineOrderService svc, CancellationToken ct) => svc.PackAsync(id, ct)).RequireAuthorization(Policies.SalesAdmin);
        staff.MapPost("/orders/{id:guid}/assign", (Guid id, AssignRequest r, OnlineOrderService svc, CancellationToken ct) => svc.AssignAsync(id, r.AgentUserId, ct)).RequireAuthorization(Policies.SalesAdmin);
        staff.MapPost("/orders/{id:guid}/complete", (Guid id, CompleteRequest r, OnlineOrderService svc, CancellationToken ct) => svc.CompleteAsync(id, r, ct)).RequireAuthorization(Policies.Delivery);
        staff.MapPost("/orders/{id:guid}/fail", (Guid id, ReasonRequest r, OnlineOrderService svc, CancellationToken ct) => svc.FailDeliveryAsync(id, r.Reason, ct)).RequireAuthorization(Policies.Delivery);
        staff.MapPost("/orders/{id:guid}/cancel", (Guid id, ReasonRequest r, OnlineOrderService svc, CancellationToken ct) => svc.CancelAsync(id, r.Reason, ct)).RequireAuthorization(Policies.SalesAdmin);
        staff.MapGet("/orders/{id:guid}/files/{fileId:guid}", async (Guid id, Guid fileId, OnlineOrderService svc, CancellationToken ct) =>
        {
            var (stream, type, name) = await svc.FileAsync(id, fileId, ct);
            return Results.File(stream, type, name);
        }).RequireAuthorization(Policies.SalesAdmin);
        staff.MapGet("/agents", (OnlineOrderService svc, CancellationToken ct) => svc.AgentsAsync(ct)).RequireAuthorization(Policies.SalesAdmin);
        staff.MapGet("/deliveries", (OnlineOrderService svc, CancellationToken ct) => svc.MyDeliveriesAsync(ct)).RequireAuthorization(Policies.Delivery);
        staff.MapGet("/collections", (DateOnly date, OnlineOrderService svc, CancellationToken ct) => svc.CollectionsAsync(date, ct)).RequireAuthorization(Policies.Reports);
        staff.MapPut("/settings/{branchId:guid}", async (Guid branchId, OnlineSettingsRequest r, OnlineOrderService svc, CancellationToken ct) =>
        {
            await svc.SaveSettingsAsync(branchId, r, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Admin);
    }
}
