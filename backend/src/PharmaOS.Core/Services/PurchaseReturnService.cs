using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record ReturnCandidate(Guid BatchId, Guid ProductId, string ProductName, string? Manufacturer, string BatchNo,
    DateOnly ExpiryDate, int DaysToExpiry, decimal QtyOnHand, decimal QtyInOpenReturns, decimal CostPerUnit, decimal GstRate,
    decimal ClaimValue, Guid? SuggestedSupplierId, string? SuggestedSupplierName);

public record PurchaseReturnLineInput(Guid BatchId, decimal QtyUnits);
public record PurchaseReturnInput(Guid SupplierId, PurchaseReturnReason Reason, string? Notes, List<PurchaseReturnLineInput> Lines);
public record SettleReturnRequest(string CreditNoteNo, DateOnly CreditNoteDate, decimal AcceptedAmount, string? Notes);

public record PurchaseReturnLineDto(Guid Id, int LineNo, Guid ProductId, string ProductName, Guid BatchId, string BatchNo,
    DateOnly ExpiryDate, decimal QtyUnits, decimal CostPerUnit, decimal GstRate, decimal ClaimAmount);
public record PurchaseReturnDto(Guid Id, string ReturnNo, Guid SupplierId, string SupplierName, PurchaseReturnReason Reason,
    PurchaseReturnStatus Status, decimal ClaimAmount, decimal AcceptedAmount, decimal WrittenOffAmount, string? SupplierCreditNoteNo,
    DateOnly? CreditNoteDate, string? Notes, string? RejectReason, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt,
    DateTimeOffset? SettledAt, int Version, List<PurchaseReturnLineDto> Lines);
public record PurchaseReturnListItem(Guid Id, string ReturnNo, string SupplierName, PurchaseReturnReason Reason,
    PurchaseReturnStatus Status, decimal ClaimAmount, decimal AcceptedAmount, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt, int AgeDays);

public record RecoveryReport(decimal PendingClaims, int PendingCount, decimal Claimed, decimal Accepted, decimal WrittenOff,
    decimal RecoveryPercent, List<AgingBucket> PendingAging, List<SupplierRecovery> BySupplier);
public record AgingBucket(string Label, decimal Amount, int Count);
public record SupplierRecovery(Guid SupplierId, string SupplierName, decimal Pending, decimal Claimed, decimal Accepted, decimal WrittenOff);

public class PurchaseReturnService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);

    /// <summary>Batches with stock expiring within N days (or already expired), with the supplier they were last bought from.</summary>
    public async Task<List<ReturnCandidate>> CandidatesAsync(int days, CancellationToken ct = default)
    {
        var today = Today;
        var until = today.AddDays(Math.Clamp(days, 0, 730));
        var stock = await db.StockBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.QtyUnits > 0 && b.Batch!.ExpiryDate <= until)
            .Select(b => new
            {
                b.BatchId, b.ProductId, b.Batch!.Product!.Name, b.Batch.Product.Manufacturer, b.Batch.Product.GstRate,
                b.Batch.BatchNo, b.Batch.ExpiryDate, b.Batch.CostPerUnit, b.QtyUnits,
            })
            .OrderBy(x => x.ExpiryDate).ToListAsync(ct);
        var batchIds = stock.Select(s => s.BatchId).ToList();

        var lastSupplier = await db.PurchaseInvoiceLines.AsNoTracking()
            .Where(l => l.BatchId != null && batchIds.Contains(l.BatchId!.Value))
            .Join(db.PurchaseInvoices.Where(p => p.Status == PurchaseStatus.Posted), l => l.PurchaseInvoiceId, p => p.Id,
                (l, p) => new { BatchId = l.BatchId!.Value, p.SupplierId, SupplierName = p.Supplier!.Name, p.InvoiceDate })
            .ToListAsync(ct);
        var supplierByBatch = lastSupplier.GroupBy(x => x.BatchId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.InvoiceDate).First());

        var inOpen = await db.PurchaseReturnLines.AsNoTracking()
            .Where(l => batchIds.Contains(l.BatchId))
            .Join(db.PurchaseReturns.Where(r => r.Status == PurchaseReturnStatus.Draft && r.BranchId == ctx.BranchId),
                l => l.PurchaseReturnId, r => r.Id, (l, r) => new { l.BatchId, l.QtyUnits })
            .GroupBy(x => x.BatchId).Select(g => new { g.Key, Qty = g.Sum(x => x.QtyUnits) })
            .ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

        return stock.Select(s =>
        {
            supplierByBatch.TryGetValue(s.BatchId, out var sup);
            return new ReturnCandidate(s.BatchId, s.ProductId, s.Name, s.Manufacturer, s.BatchNo, s.ExpiryDate,
                s.ExpiryDate.DayNumber - today.DayNumber, s.QtyUnits, inOpen.GetValueOrDefault(s.BatchId), s.CostPerUnit,
                s.GstRate, ClaimFor(s.QtyUnits, s.CostPerUnit, s.GstRate), sup?.SupplierId, sup?.SupplierName);
        }).ToList();
    }

    private static decimal ClaimFor(decimal qty, decimal costPerUnit, decimal gst)
    {
        var taxable = Money.R2(qty * costPerUnit);
        return taxable + Money.TaxOnExclusive(taxable, gst);
    }

    public async Task<PurchaseReturnDto> CreateDraftAsync(PurchaseReturnInput input, CancellationToken ct = default)
    {
        Require(Enum.IsDefined(input.Reason), "Invalid reason.");
        Require(input.Lines is { Count: > 0 and <= 500 }, "Select 1-500 batches to return.");
        Require(input.Lines.Select(l => l.BatchId).Distinct().Count() == input.Lines.Count, "Each batch can appear only once.");
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == input.SupplierId, ct) ?? throw BusinessException.NotFound("Supplier");

        var batchIds = input.Lines.Select(l => l.BatchId).ToList();
        var batches = await db.Batches.Include(b => b.Product).Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var onHand = await db.StockBalances.Where(b => b.BranchId == ctx.BranchId && batchIds.Contains(b.BatchId))
            .ToDictionaryAsync(b => b.BatchId, b => b.QtyUnits, ct);

        var today = Today;
        var r = new PurchaseReturn
        {
            BranchId = ctx.BranchId, SupplierId = supplier.Id, Supplier = supplier, Reason = input.Reason,
            Notes = input.Notes?.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
        };
        var n = 0;
        foreach (var l in input.Lines)
        {
            n++;
            if (!batches.TryGetValue(l.BatchId, out var batch)) throw BusinessException.Validation($"Line {n}: batch not found.");
            Require(l.QtyUnits > 0 && l.QtyUnits == decimal.Truncate(l.QtyUnits), $"Line {n}: quantity must be a positive whole number.");
            var available = onHand.GetValueOrDefault(l.BatchId);
            if (l.QtyUnits > available)
                throw BusinessException.Validation($"Line {n}: only {available} units of {batch.Product!.Name} batch {batch.BatchNo} on hand.");
            var gst = batch.Product!.GstRate;
            r.Lines.Add(new PurchaseReturnLine
            {
                PurchaseReturnId = r.Id, LineNo = n, ProductId = batch.ProductId, BatchId = batch.Id, QtyUnits = l.QtyUnits,
                CostPerUnit = batch.CostPerUnit, GstRate = gst, ClaimAmount = ClaimFor(l.QtyUnits, batch.CostPerUnit, gst),
            });
        }
        r.ClaimAmount = r.Lines.Sum(x => x.ClaimAmount);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        r.ReturnNo = SequenceService.Format(branch.Code, "D", today, await sequences.NextAsync(branch.Id, "D", today, ct));
        db.PurchaseReturns.Add(r);
        audit.Record("purchase_return.created", nameof(PurchaseReturn), r.Id, $"{r.ReturnNo} to {supplier.Name} claim {r.ClaimAmount}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(r.Id, ct);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var r = await Load(id, ct);
        if (r.Status != PurchaseReturnStatus.Draft) throw BusinessException.Conflict("not_draft", "Only draft returns can be deleted.");
        db.PurchaseReturnLines.RemoveRange(r.Lines);
        db.PurchaseReturns.Remove(r);
        audit.Record("purchase_return.deleted", nameof(PurchaseReturn), r.Id, r.ReturnNo);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Goods physically leave the store: stock goes out, claim becomes a pending recovery.</summary>
    public async Task<PurchaseReturnDto> DispatchAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await Load(id, ct);
        if (r.Status != PurchaseReturnStatus.Draft) throw BusinessException.Conflict("not_draft", "This return has already been dispatched.");
        foreach (var l in r.Lines)
            await inventory.ApplyAsync(r.BranchId, l.Batch!, -l.QtyUnits, MovementType.PurchaseReturnOut, nameof(PurchaseReturn),
                r.Id, l.Id, r.ReturnNo, ct);
        r.Status = PurchaseReturnStatus.Dispatched;
        r.DispatchedAt = clock.UtcNow;
        audit.Record("purchase_return.dispatched", nameof(PurchaseReturn), r.Id, r.ReturnNo);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Supplier issued a credit note. Accepted may be less than claimed; the difference is written off.</summary>
    public async Task<PurchaseReturnDto> SettleAsync(Guid id, SettleReturnRequest req, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(req.CreditNoteNo) && req.CreditNoteNo.Length <= 50, "Supplier credit note number is required.");
        Require(req.AcceptedAmount >= 0, "Accepted amount cannot be negative.");
        Require(req.CreditNoteDate <= Today, "Credit note date cannot be in the future.");
        var r = await Load(id, ct);
        if (r.Status != PurchaseReturnStatus.Dispatched) throw BusinessException.Conflict("not_dispatched", "Only dispatched returns can be settled.");
        var accepted = Money.R2(req.AcceptedAmount);
        if (accepted > r.ClaimAmount)
            throw BusinessException.Validation($"Accepted amount cannot exceed the claim ({r.ClaimAmount:0.00}). Record any extra as a separate adjustment.");
        r.AcceptedAmount = accepted;
        r.WrittenOffAmount = r.ClaimAmount - accepted;
        r.SupplierCreditNoteNo = req.CreditNoteNo.Trim().ToUpperInvariant();
        r.CreditNoteDate = req.CreditNoteDate;
        if (!string.IsNullOrWhiteSpace(req.Notes)) r.Notes = req.Notes.Trim();
        r.Status = PurchaseReturnStatus.Settled;
        r.SettledAt = clock.UtcNow;
        audit.Record("purchase_return.settled", nameof(PurchaseReturn), r.Id,
            $"{r.ReturnNo} CN {r.SupplierCreditNoteNo} accepted {accepted} written off {r.WrittenOffAmount}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Supplier refused the return and sent the goods back: stock re-enters the same batches.</summary>
    public async Task<PurchaseReturnDto> RejectAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var r = await Load(id, ct);
        if (r.Status != PurchaseReturnStatus.Dispatched) throw BusinessException.Conflict("not_dispatched", "Only dispatched returns can be rejected.");
        foreach (var l in r.Lines)
            await inventory.ApplyAsync(r.BranchId, l.Batch!, l.QtyUnits, MovementType.PurchaseReturnRejectedIn, nameof(PurchaseReturn),
                r.Id, l.Id, $"{r.ReturnNo} rejected: {reason}", ct);
        r.Status = PurchaseReturnStatus.Rejected;
        r.RejectReason = reason.Trim();
        r.SettledAt = clock.UtcNow;
        audit.Record("purchase_return.rejected", nameof(PurchaseReturn), r.Id, $"{r.ReturnNo}: {reason}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<PurchaseReturnListItem>> ListAsync(PurchaseReturnStatus? status, Guid? supplierId, int page, int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.PurchaseReturns.AsNoTracking().Where(r => r.BranchId == ctx.BranchId);
        if (status is not null) q = q.Where(r => r.Status == status);
        if (supplierId is not null) q = q.Where(r => r.SupplierId == supplierId);
        var total = await q.CountAsync(ct);
        var now = clock.UtcNow;
        var rows = await q.OrderByDescending(r => r.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(r => new { r.Id, r.ReturnNo, r.Supplier!.Name, r.Reason, r.Status, r.ClaimAmount, r.AcceptedAmount, r.CreatedAt, r.DispatchedAt })
            .ToListAsync(ct);
        return new PagedResult<PurchaseReturnListItem>(rows.Select(r => new PurchaseReturnListItem(r.Id, r.ReturnNo, r.Name, r.Reason,
            r.Status, r.ClaimAmount, r.AcceptedAmount, r.CreatedAt, r.DispatchedAt,
            r.DispatchedAt is null ? 0 : (int)(now - r.DispatchedAt.Value).TotalDays)).ToList(), total, page, pageSize);
    }

    public async Task<PurchaseReturnDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.PurchaseReturns.AsNoTracking().Include(x => x.Supplier)
            .Include(x => x.Lines).ThenInclude(l => l.Product).Include(x => x.Lines).ThenInclude(l => l.Batch)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct)
            ?? throw BusinessException.NotFound("Purchase return");
        return new PurchaseReturnDto(r.Id, r.ReturnNo, r.SupplierId, r.Supplier!.Name, r.Reason, r.Status, r.ClaimAmount,
            r.AcceptedAmount, r.WrittenOffAmount, r.SupplierCreditNoteNo, r.CreditNoteDate, r.Notes, r.RejectReason, r.CreatedAt,
            r.DispatchedAt, r.SettledAt, r.Version,
            r.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseReturnLineDto(l.Id, l.LineNo, l.ProductId, l.Product!.Name, l.BatchId,
                l.Batch!.BatchNo, l.Batch.ExpiryDate, l.QtyUnits, l.CostPerUnit, l.GstRate, l.ClaimAmount)).ToList());
    }

    /// <summary>
    /// Recovery = accepted ÷ claimed for settled returns (rejected excluded, since goods came back).
    /// Pending = dispatched, not yet settled; aged from dispatch date.
    /// </summary>
    public async Task<RecoveryReport> RecoveryAsync(CancellationToken ct = default)
    {
        var rows = await db.PurchaseReturns.AsNoTracking().Where(r => r.BranchId == ctx.BranchId && r.Status != PurchaseReturnStatus.Draft)
            .Select(r => new { r.SupplierId, r.Supplier!.Name, r.Status, r.ClaimAmount, r.AcceptedAmount, r.WrittenOffAmount, r.DispatchedAt })
            .ToListAsync(ct);
        var now = clock.UtcNow;
        var pending = rows.Where(r => r.Status == PurchaseReturnStatus.Dispatched).ToList();
        var settled = rows.Where(r => r.Status == PurchaseReturnStatus.Settled).ToList();
        AgingBucket Bucket(string label, int from, int to)
        {
            var b = pending.Where(p => { var d = (now - p.DispatchedAt!.Value).TotalDays; return d >= from && d <= to; }).ToList();
            return new AgingBucket(label, b.Sum(x => x.ClaimAmount), b.Count);
        }
        var claimed = settled.Sum(x => x.ClaimAmount);
        var accepted = settled.Sum(x => x.AcceptedAmount);
        return new RecoveryReport(pending.Sum(x => x.ClaimAmount), pending.Count, claimed, accepted, settled.Sum(x => x.WrittenOffAmount),
            claimed == 0 ? 0 : Money.R2(accepted * 100 / claimed),
            [Bucket("0-30 days", 0, 30), Bucket("31-60 days", 31, 60), Bucket("61-90 days", 61, 90), Bucket("90+ days", 91, int.MaxValue)],
            rows.GroupBy(r => new { r.SupplierId, r.Name }).Select(g => new SupplierRecovery(g.Key.SupplierId, g.Key.Name,
                g.Where(x => x.Status == PurchaseReturnStatus.Dispatched).Sum(x => x.ClaimAmount),
                g.Where(x => x.Status == PurchaseReturnStatus.Settled).Sum(x => x.ClaimAmount),
                g.Where(x => x.Status == PurchaseReturnStatus.Settled).Sum(x => x.AcceptedAmount),
                g.Where(x => x.Status == PurchaseReturnStatus.Settled).Sum(x => x.WrittenOffAmount)))
                .OrderByDescending(x => x.Pending).ToList());
    }

    private async Task<PurchaseReturn> Load(Guid id, CancellationToken ct) =>
        await db.PurchaseReturns.Include(r => r.Lines).ThenInclude(l => l.Batch)
            .SingleOrDefaultAsync(r => r.Id == id && r.BranchId == ctx.BranchId, ct)
        ?? throw BusinessException.NotFound("Purchase return");
}
