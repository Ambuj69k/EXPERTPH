using PharmaOS.Core.Domain;
using PharmaOS.Core.Services;

namespace PharmaOS.Api;

public record OverrideRequest(string? CreditOverrideReason);

/// <summary>Distributor (B2B) module endpoints.</summary>
public static class DistributorEndpoints
{
    public static void MapDistributorEndpoints(this WebApplication app)
    {
        var d = app.MapGroup("/api/v1/distribution").RequireAuthorization(Policies.Tenant).RequireModule(Plans.Distributor);

        d.MapGet("/retailers", (string? q, DistributorService s, CancellationToken ct) => s.RetailersAsync(q, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/retailers", (RetailerInput r, DistributorService s, CancellationToken ct) => s.SaveRetailerAsync(null, r, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapPut("/retailers/{id:guid}", (Guid id, RetailerInput r, DistributorService s, CancellationToken ct) => s.SaveRetailerAsync(id, r, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapGet("/retailers/{id:guid}/statement", (Guid id, DistributorService s, CancellationToken ct) => s.StatementAsync(id, ct)).RequireAuthorization(Policies.Trade);

        d.MapGet("/rates", (string? q, Guid? retailerId, DistributorService s, CancellationToken ct) => s.RatesAsync(q, retailerId, ct)).RequireAuthorization(Policies.Trade);
        d.MapPut("/rates", async (TradeRateInput r, DistributorService s, CancellationToken ct) => { await s.SetRateAsync(r, ct); return Results.NoContent(); })
            .RequireAuthorization(Policies.TradeAdmin);
        d.MapGet("/schemes", (DistributorService s, CancellationToken ct) => s.SchemesAsync(ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/schemes", (SchemeInput r, DistributorService s, CancellationToken ct) => s.SaveSchemeAsync(null, r, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapPut("/schemes/{id:guid}", (Guid id, SchemeInput r, DistributorService s, CancellationToken ct) => s.SaveSchemeAsync(id, r, ct)).RequireAuthorization(Policies.TradeAdmin);

        d.MapGet("/orders", (SalesOrderStatus? status, DistributorService s, CancellationToken ct) => s.OrdersAsync(status, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/orders", (SalesOrderInput r, DistributorService s, CancellationToken ct) => s.CreateOrderAsync(r, ct)).RequireAuthorization(Policies.Trade);
        d.MapGet("/orders/{id:guid}", (Guid id, DistributorService s, CancellationToken ct) => s.GetOrderAsync(id, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/orders/{id:guid}/cancel", async (Guid id, DistributorService s, CancellationToken ct) => { await s.CancelOrderAsync(id, ct); return Results.NoContent(); })
            .RequireAuthorization(Policies.Trade);
        d.MapPost("/orders/{id:guid}/invoice", (Guid id, OverrideRequest r, DistributorService s, CancellationToken ct) => s.InvoiceOrderAsync(id, r.CreditOverrideReason, ct))
            .RequireAuthorization(Policies.TradeAdmin);

        d.MapGet("/invoices", (Guid? retailerId, DistributorService s, CancellationToken ct) => s.InvoicesAsync(retailerId, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/invoices", (TradeInvoiceRequest r, DistributorService s, CancellationToken ct) => s.CreateInvoiceAsync(r, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapGet("/invoices/{id:guid}", (Guid id, DistributorService s, CancellationToken ct) => s.GetInvoiceAsync(id, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/invoices/{id:guid}/cancel", (Guid id, ReasonRequest r, DistributorService s, CancellationToken ct) => s.CancelInvoiceAsync(id, r.Reason, ct))
            .RequireAuthorization(Policies.TradeAdmin);
        d.MapPost("/credit-notes", (CreditNoteRequest r, DistributorService s, CancellationToken ct) => s.CreateCreditNoteAsync(r, ct)).RequireAuthorization(Policies.TradeAdmin);

        d.MapGet("/receipts", (Guid? retailerId, DistributorService s, CancellationToken ct) => s.ReceiptsAsync(retailerId, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/receipts", (TradeReceiptRequest r, DistributorService s, CancellationToken ct) => s.CreateReceiptAsync(r, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/receipts/{id:guid}/cancel", (Guid id, ReasonRequest r, DistributorService s, CancellationToken ct) => s.CancelReceiptAsync(id, r.Reason, ct))
            .RequireAuthorization(Policies.TradeAdmin);

        d.MapGet("/dispatch/pending", (DistributorService s, CancellationToken ct) => s.PendingDispatchAsync(ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapGet("/dispatches", (DispatchStatus? status, DistributorService s, CancellationToken ct) => s.DispatchesAsync(status, ct)).RequireAuthorization(Policies.Trade);
        d.MapGet("/dispatches/{id:guid}", (Guid id, DistributorService s, CancellationToken ct) => s.GetDispatchAsync(id, ct)).RequireAuthorization(Policies.Trade);
        d.MapGet("/dispatches/{id:guid}/pick-list", (Guid id, DistributorService s, CancellationToken ct) => s.PickListAsync(id, ct)).RequireAuthorization(Policies.Trade);
        d.MapPost("/dispatches", (DispatchInput r, DistributorService s, CancellationToken ct) => s.CreateDispatchAsync(r, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapPost("/dispatches/{id:guid}/depart", (Guid id, DistributorService s, CancellationToken ct) => s.DepartAsync(id, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapPost("/dispatches/{id:guid}/cancel", (Guid id, DistributorService s, CancellationToken ct) => s.CancelDispatchAsync(id, ct)).RequireAuthorization(Policies.TradeAdmin);
        d.MapPost("/dispatches/{id:guid}/stops/{stopId:guid}", (Guid id, Guid stopId, StopConfirmInput r, DistributorService s, CancellationToken ct) => s.ConfirmStopAsync(id, stopId, r, ct))
            .RequireAuthorization(Policies.Trade);

        d.MapGet("/reports/aging", (DistributorService s, CancellationToken ct) => s.AgingAsync(ct)).RequireAuthorization(Policies.Trade);
        d.MapGet("/reports/salesmen", (DateOnly from, DateOnly to, DistributorService s, CancellationToken ct) => s.SalesmanReportAsync(from, to, ct))
            .RequireAuthorization(Policies.TradeAdmin);
    }
}
