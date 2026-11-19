using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record DispatchInput(string? Route, string? Vehicle, string? DriverName, List<Guid> InvoiceIds);
public record StopConfirmInput(bool Delivered, string? ReceivedBy, string? Note);
public record DispatchStopDto(Guid Id, int Seq, Guid TradeInvoiceId, string InvoiceNo, string RetailerName, string? Address, string? Phone, decimal NetAmount,
    DeliveryStatus Status, string? ReceivedBy, string? Note, DateTimeOffset? At);
public record DispatchDto(Guid Id, string DispatchNo, DateOnly Date, string? Route, string? Vehicle, string? DriverName, DispatchStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? DepartedAt, DateTimeOffset? CompletedAt, int Version, decimal Value, List<DispatchStopDto> Stops);
public record PickLine(Guid ProductId, string ProductName, string BatchNo, DateOnly ExpiryDate, decimal Packs, decimal FreePacks, List<string> Invoices);
public record PendingInvoice(Guid Id, string InvoiceNo, DateOnly InvoiceDate, string RetailerName, string? Route, decimal NetAmount, bool PreviouslyFailed);

public partial class DistributorService
{
    // ===================== picking & dispatch =====================

    private IQueryable<Guid> ActiveOrDeliveredInvoiceIds() =>
        db.DispatchStops.Where(s => s.Status == DeliveryStatus.Delivered
                                    || (s.Status == DeliveryStatus.Pending && db.Dispatches.Any(d => d.Id == s.DispatchId && (d.Status == DispatchStatus.Packing || d.Status == DispatchStatus.OnRoute))))
            .Select(s => s.TradeInvoiceId);

    /// <summary>Posted invoices not yet delivered and not on an active trip (failed deliveries come back here).</summary>
    public async Task<List<PendingInvoice>> PendingDispatchAsync(CancellationToken ct = default)
    {
        var busy = ActiveOrDeliveredInvoiceIds();
        var rows = await db.TradeInvoices.AsNoTracking().Where(i => i.BranchId == ctx.BranchId && i.Status == TradeDocStatus.Posted && !busy.Contains(i.Id))
            .OrderBy(i => i.Retailer!.Route).ThenBy(i => i.InvoiceDate)
            .Select(i => new { i.Id, i.InvoiceNo, i.InvoiceDate, i.Retailer!.Name, i.Retailer.Route, i.NetAmount, Failed = db.DispatchStops.Any(s => s.TradeInvoiceId == i.Id && s.Status == DeliveryStatus.Failed) })
            .Take(500).ToListAsync(ct);
        return rows.Select(r => new PendingInvoice(r.Id, r.InvoiceNo, r.InvoiceDate, r.Name, r.Route, r.NetAmount, r.Failed)).ToList();
    }

    public async Task<DispatchDto> CreateDispatchAsync(DispatchInput i, CancellationToken ct = default)
    {
        var ids = i.InvoiceIds.Distinct().ToList();
        Require(ids.Count is > 0 and <= 200, "Choose 1–200 invoices.");
        var busy = ActiveOrDeliveredInvoiceIds();
        var invoices = await db.TradeInvoices.Where(x => ids.Contains(x.Id) && x.BranchId == ctx.BranchId && x.Status == TradeDocStatus.Posted && !busy.Contains(x.Id))
            .Include(x => x.Retailer).ToListAsync(ct);
        if (invoices.Count != ids.Count) throw BusinessException.Conflict("not_dispatchable", "Some invoices are cancelled, delivered, or already on another trip. Reload the list.");
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var d = new Dispatch
        {
            BranchId = branch.Id, Date = Today, Route = i.Route?.Trim(), Vehicle = i.Vehicle?.Trim().ToUpperInvariant(), DriverName = i.DriverName?.Trim(),
            CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            DispatchNo = SequenceService.Format(branch.Code, "F", Today, await sequences.NextAsync(branch.Id, "F", Today, ct)),
        };
        var seq = 0;
        d.Stops = invoices.OrderBy(x => x.Retailer!.Route).ThenBy(x => x.Retailer!.Name)
            .Select(x => new DispatchStop { DispatchId = d.Id, TradeInvoiceId = x.Id, Seq = ++seq }).ToList();
        db.Dispatches.Add(d);
        audit.Record("dispatch.created", nameof(Dispatch), d.Id, $"{d.DispatchNo}: {d.Stops.Count} invoice(s)");
        await db.SaveChangesAsync(ct);
        return await DispatchDtoAsync(d.Id, ct);
    }

    /// <summary>What to pull from the shelves: invoice lines grouped by product and batch (paid + free packs).</summary>
    public async Task<List<PickLine>> PickListAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Dispatches.AsNoTracking().Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Dispatch");
        var invIds = d.Stops.Select(s => s.TradeInvoiceId).ToList();
        var lines = await db.TradeInvoiceLines.AsNoTracking().Where(l => invIds.Contains(l.TradeInvoiceId))
            .Select(l => new { l.ProductId, l.Product!.Name, l.Batch!.BatchNo, l.Batch.ExpiryDate, l.QtyPacks, l.FreeQtyPacks, InvoiceNo = db.TradeInvoices.Where(t => t.Id == l.TradeInvoiceId).Select(t => t.InvoiceNo).First() })
            .ToListAsync(ct);
        return lines.GroupBy(l => (l.ProductId, l.Name, l.BatchNo, l.ExpiryDate))
            .Select(g => new PickLine(g.Key.ProductId, g.Key.Name, g.Key.BatchNo, g.Key.ExpiryDate, g.Sum(x => x.QtyPacks), g.Sum(x => x.FreeQtyPacks), g.Select(x => x.InvoiceNo).Distinct().ToList()))
            .OrderBy(p => p.ProductName).ThenBy(p => p.ExpiryDate).ToList();
    }

    public async Task<DispatchDto> DepartAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Dispatches.Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Dispatch");
        if (d.Status != DispatchStatus.Packing) throw BusinessException.Conflict("invalid_state", "This trip has already left.");
        var invIds = d.Stops.Select(s => s.TradeInvoiceId).ToList();
        if (await db.TradeInvoices.AnyAsync(t => invIds.Contains(t.Id) && t.Status != TradeDocStatus.Posted, ct))
            throw BusinessException.Conflict("invoice_cancelled", "An invoice on this trip was cancelled. Remove it by cancelling and re-creating the trip.");
        d.Status = DispatchStatus.OnRoute; d.DepartedAt = clock.UtcNow;
        audit.Record("dispatch.departed", nameof(Dispatch), d.Id, $"{d.DispatchNo} {d.Vehicle}");
        await SaveDispatchAsync(ct);
        return await DispatchDtoAsync(d.Id, ct);
    }

    public async Task<DispatchDto> ConfirmStopAsync(Guid id, Guid stopId, StopConfirmInput i, CancellationToken ct = default)
    {
        var d = await db.Dispatches.Include(x => x.Stops).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Dispatch");
        if (d.Status != DispatchStatus.OnRoute) throw BusinessException.Conflict("invalid_state", "Deliveries are confirmed only while the trip is on route.");
        var s = d.Stops.SingleOrDefault(x => x.Id == stopId) ?? throw BusinessException.NotFound("Stop");
        if (s.Status != DeliveryStatus.Pending) throw BusinessException.Conflict("invalid_state", "This delivery is already confirmed.");
        if (i.Delivered) Require(!string.IsNullOrWhiteSpace(i.ReceivedBy), "Who received the goods?");
        else Require(!string.IsNullOrWhiteSpace(i.Note), "Why did the delivery fail?");
        s.Status = i.Delivered ? DeliveryStatus.Delivered : DeliveryStatus.Failed;
        s.ReceivedBy = i.ReceivedBy?.Trim(); s.Note = i.Note?.Trim(); s.At = clock.UtcNow;
        if (d.Stops.All(x => x.Status != DeliveryStatus.Pending)) { d.Status = DispatchStatus.Completed; d.CompletedAt = clock.UtcNow; }
        else db.Entry(d).Property(x => x.Status).IsModified = true; // version bump: two people cannot confirm the same trip blindly
        audit.Record(i.Delivered ? "dispatch.delivered" : "dispatch.failed", nameof(Dispatch), d.Id, $"{d.DispatchNo} stop {s.Seq}");
        await SaveDispatchAsync(ct);
        return await DispatchDtoAsync(d.Id, ct);
    }

    public async Task<DispatchDto> CancelDispatchAsync(Guid id, CancellationToken ct = default)
    {
        var d = await db.Dispatches.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Dispatch");
        if (d.Status != DispatchStatus.Packing) throw BusinessException.Conflict("invalid_state", "Only trips still being packed can be cancelled.");
        d.Status = DispatchStatus.Cancelled;
        audit.Record("dispatch.cancelled", nameof(Dispatch), d.Id, d.DispatchNo);
        await SaveDispatchAsync(ct);
        return await DispatchDtoAsync(d.Id, ct);
    }

    public async Task<List<DispatchDto>> DispatchesAsync(DispatchStatus? status, CancellationToken ct = default)
    {
        var q = db.Dispatches.AsNoTracking().Where(d => d.BranchId == ctx.BranchId);
        q = status is { } s ? q.Where(d => d.Status == s) : q.Where(d => d.CreatedAt >= clock.UtcNow.AddDays(-30) || d.Status == DispatchStatus.Packing || d.Status == DispatchStatus.OnRoute);
        var ids = await q.OrderByDescending(d => d.CreatedAt).Select(d => d.Id).Take(200).ToListAsync(ct);
        var list = new List<DispatchDto>();
        foreach (var id in ids) list.Add(await DispatchDtoAsync(id, ct));
        return list;
    }

    public Task<DispatchDto> GetDispatchAsync(Guid id, CancellationToken ct = default) => DispatchDtoAsync(id, ct);

    private async Task SaveDispatchAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "Someone else updated this trip. Reload."); }
    }

    private async Task<DispatchDto> DispatchDtoAsync(Guid id, CancellationToken ct)
    {
        var d = await db.Dispatches.AsNoTracking().Include(x => x.Stops).ThenInclude(s => s.Invoice).ThenInclude(i => i!.Retailer).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw BusinessException.NotFound("Dispatch");
        var stops = d.Stops.OrderBy(s => s.Seq).Select(s => new DispatchStopDto(s.Id, s.Seq, s.TradeInvoiceId, s.Invoice!.InvoiceNo, s.Invoice.Retailer!.Name, s.Invoice.Retailer.Address,
            s.Invoice.Retailer.Phone, s.Invoice.NetAmount, s.Status, s.ReceivedBy, s.Note, s.At)).ToList();
        return new DispatchDto(d.Id, d.DispatchNo, d.Date, d.Route, d.Vehicle, d.DriverName, d.Status, d.CreatedAt, d.DepartedAt, d.CompletedAt, d.Version, stops.Sum(s => s.NetAmount), stops);
    }
}
