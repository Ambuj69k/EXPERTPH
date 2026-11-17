using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------

public record OpenShiftRequest(decimal OpeningCash);
public record CashMovementRequest(CashMovementKind Kind, decimal Amount, string Reason);
public record CloseShiftRequest(decimal CountedCash, string? Notes);
public record CashMovementDto(CashMovementKind Kind, decimal Amount, string Reason, DateTimeOffset At);
public record ShiftSummary(Guid Id, string ShiftNo, Guid UserId, string UserName, ShiftStatus Status, DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt, int BillCount, decimal OpeningCash, decimal CashSales, decimal CancelledCash, decimal CashReceipts,
    decimal CashRefunds, decimal CashSupplierPayments, decimal PayIns, decimal PayOuts, decimal ExpectedCash, decimal? CountedCash,
    decimal? Variance, decimal UpiSales, decimal CardSales, decimal CreditSales, string? CloseNotes, List<CashMovementDto> Movements);
public record ShiftListItem(Guid Id, string ShiftNo, string UserName, ShiftStatus Status, DateTimeOffset OpenedAt, DateTimeOffset? ClosedAt,
    decimal? ExpectedCash, decimal? CountedCash, decimal? Variance);

public record TenantSettings(decimal CashierMaxDiscountPercent, bool RequireOpenShift);

public record CreateCountRequest(string Title, List<Guid>? ProductIds, string? Search);
public record CountEntry(Guid LineId, decimal? CountedQty);
public record CountLineDto(Guid Id, Guid ProductId, string ProductName, string BaseUnit, Guid BatchId, string BatchNo, DateOnly ExpiryDate,
    decimal SystemQtyNow, decimal? SystemQtyAtCount, decimal? CountedQty, decimal? Variance, decimal CostPerUnit);
public record StockCountDto(Guid Id, string CountNo, string Title, StockCountStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? PostedAt,
    int LineCount, int CountedLines, decimal GainValue, decimal LossValue, List<CountLineDto> Lines);
public record StockCountListItem(Guid Id, string CountNo, string Title, StockCountStatus Status, DateTimeOffset CreatedAt, int LineCount, int CountedLines);

public record BatchSearchRow(Guid BatchId, Guid ProductId, string ProductName, string? Manufacturer, string BatchNo, DateOnly ExpiryDate,
    decimal Mrp, bool IsBlocked, string? BlockReason, decimal QtyOnHand);
public record TraceSale(Guid InvoiceId, string InvoiceNo, DateTimeOffset At, string? CustomerName, string? CustomerPhone, decimal QtyUnits,
    decimal ReturnedQtyUnits, SalesStatus Status);
public record TracePurchase(Guid PurchaseId, string SupplierName, string SupplierInvoiceNo, DateOnly InvoiceDate, decimal QtyUnits);
public record BatchTrace(BatchSearchRow Batch, List<TracePurchase> Purchases, List<TraceSale> Sales, decimal QtySold, decimal QtyReturnedToSupplier);

/// <summary>Cash drawer shifts and tenant-level counter settings.</summary>
public class ShiftService(PharmaDbContext db, ITenantContext ctx, IClock clock, SequenceService sequences, AuditService audit)
{
    public async Task<TenantSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == ctx.TenantId, ct);
        return new TenantSettings(t.CashierMaxDiscountPercent, t.RequireOpenShift);
    }

    public async Task<TenantSettings> UpdateSettingsAsync(TenantSettings s, CancellationToken ct = default)
    {
        Require(s.CashierMaxDiscountPercent is >= 0 and <= 100, "Discount limit must be 0-100%.");
        var t = await db.Tenants.SingleAsync(x => x.Id == ctx.TenantId, ct);
        t.CashierMaxDiscountPercent = s.CashierMaxDiscountPercent;
        t.RequireOpenShift = s.RequireOpenShift;
        audit.Record("settings.updated", nameof(Tenant), t.Id, $"cashier discount {s.CashierMaxDiscountPercent}%, shift required {s.RequireOpenShift}");
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<ShiftSummary?> CurrentAsync(CancellationToken ct = default)
    {
        var s = await db.CashShifts.AsNoTracking().SingleOrDefaultAsync(x => x.BranchId == ctx.BranchId && x.UserId == ctx.UserId && x.Status == ShiftStatus.Open, ct);
        return s is null ? null : await SummarizeAsync(s, ct);
    }

    public async Task<ShiftSummary> OpenAsync(OpenShiftRequest r, CancellationToken ct = default)
    {
        Require(r.OpeningCash >= 0, "Opening cash cannot be negative.");
        if (await db.CashShifts.AnyAsync(x => x.BranchId == ctx.BranchId && x.UserId == ctx.UserId && x.Status == ShiftStatus.Open, ct))
            throw BusinessException.Conflict("shift_open", "You already have an open shift. Close it first.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var now = clock.UtcNow;
        var today = BusinessCalendar.LocalDate(now);
        var s = new CashShift
        {
            BranchId = branch.Id, UserId = ctx.UserId, OpenedAt = now, OpeningCash = Money.R2(r.OpeningCash),
            ShiftNo = SequenceService.Format(branch.Code, "H", today, await sequences.NextAsync(branch.Id, "H", today, ct)),
        };
        db.CashShifts.Add(s);
        audit.Record("shift.opened", nameof(CashShift), s.Id, $"{s.ShiftNo} opening {s.OpeningCash}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await SummarizeAsync(s, ct);
    }

    public async Task<ShiftSummary> AddMovementAsync(CashMovementRequest r, CancellationToken ct = default)
    {
        Require(Enum.IsDefined(r.Kind), "Invalid kind.");
        Require(r.Amount > 0, "Amount must be positive.");
        Require(!string.IsNullOrWhiteSpace(r.Reason), "Reason is required.");
        var s = await db.CashShifts.SingleOrDefaultAsync(x => x.BranchId == ctx.BranchId && x.UserId == ctx.UserId && x.Status == ShiftStatus.Open, ct)
                ?? throw BusinessException.Conflict("no_shift", "Open a shift first.");
        db.CashMovements.Add(new CashMovement
        {
            CashShiftId = s.Id, Kind = r.Kind, Amount = Money.R2(r.Amount), Reason = r.Reason.Trim(), At = clock.UtcNow, UserId = ctx.UserId,
        });
        audit.Record(r.Kind == CashMovementKind.PayIn ? "shift.pay_in" : "shift.pay_out", nameof(CashShift), s.Id, $"{r.Amount} {r.Reason}");
        await db.SaveChangesAsync(ct);
        return await SummarizeAsync(s, ct);
    }

    public async Task<ShiftSummary> CloseAsync(CloseShiftRequest r, CancellationToken ct = default)
    {
        Require(r.CountedCash >= 0, "Counted cash cannot be negative.");
        var s = await db.CashShifts.SingleOrDefaultAsync(x => x.BranchId == ctx.BranchId && x.UserId == ctx.UserId && x.Status == ShiftStatus.Open, ct)
                ?? throw BusinessException.Conflict("no_shift", "You have no open shift.");
        var now = clock.UtcNow;
        s.ClosedAt = now; // freeze the window before computing
        var summary = await SummarizeAsync(s, ct);
        s.Status = ShiftStatus.Closed;
        s.ExpectedCash = summary.ExpectedCash;
        s.CountedCash = Money.R2(r.CountedCash);
        s.Variance = s.CountedCash - s.ExpectedCash;
        s.CloseNotes = r.Notes?.Trim();
        audit.Record("shift.closed", nameof(CashShift), s.Id, $"{s.ShiftNo} expected {s.ExpectedCash} counted {s.CountedCash} variance {s.Variance}");
        await db.SaveChangesAsync(ct);
        return await SummarizeAsync(s, ct);
    }

    public async Task<ShiftSummary> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.CashShifts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct)
                ?? throw BusinessException.NotFound("Shift");
        if (ctx.Role is not (UserRole.Owner or UserRole.Pharmacist) && s.UserId != ctx.UserId) throw BusinessException.NotFound("Shift");
        return await SummarizeAsync(s, ct);
    }

    public async Task<List<ShiftListItem>> ListAsync(DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var q = db.CashShifts.AsNoTracking().Where(s => s.BranchId == ctx.BranchId);
        if (from is not null) { var f = new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5)); q = q.Where(s => s.OpenedAt >= f); }
        if (to is not null) { var t = new DateTimeOffset(to.Value.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(5.5)); q = q.Where(s => s.OpenedAt <= t); }
        return await q.OrderByDescending(s => s.OpenedAt).Take(200)
            .Join(db.Users, s => s.UserId, u => u.Id, (s, u) => new ShiftListItem(s.Id, s.ShiftNo, u.DisplayName, s.Status, s.OpenedAt, s.ClosedAt,
                s.ExpectedCash, s.CountedCash, s.Variance))
            .ToListAsync(ct);
    }

    private async Task<ShiftSummary> SummarizeAsync(CashShift s, CancellationToken ct)
    {
        var from = s.OpenedAt;
        var to = s.ClosedAt ?? clock.UtcNow;
        var uid = s.UserId;
        var b = s.BranchId;

        var sales = db.SalesInvoices.AsNoTracking().Where(i => i.BranchId == b && i.CreatedByUserId == uid && i.InvoiceAt >= from && i.InvoiceAt <= to);
        var billCount = await sales.CountAsync(ct);
        var byMode = await db.SalesPayments.AsNoTracking().Where(p => sales.Select(i => i.Id).Contains(p.SalesInvoiceId))
            .GroupBy(p => p.Mode).Select(g => new { g.Key, Amount = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Amount, ct);
        var cancelledCash = await db.SalesPayments.AsNoTracking()
            .Where(p => p.Mode == PaymentMode.Cash && db.SalesInvoices.Any(i => i.Id == p.SalesInvoiceId && i.BranchId == b
                && i.CancelledByUserId == uid && i.CancelledAt >= from && i.CancelledAt <= to))
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var refunds = await db.SalesReturns.AsNoTracking()
            .Where(r => r.BranchId == b && r.CreatedByUserId == uid && r.RefundMode == PaymentMode.Cash && r.ReturnAt >= from && r.ReturnAt <= to)
            .SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0;
        var receipts = await db.CustomerReceipts.AsNoTracking()
            .Where(r => r.BranchId == b && r.CreatedByUserId == uid && r.Status == DocStatus.Posted && r.Mode == SettlementMode.Cash && r.CreatedAt >= from && r.CreatedAt <= to)
            .SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
        var supplierCash = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.BranchId == b && p.CreatedByUserId == uid && p.Status == DocStatus.Posted && p.Mode == SettlementMode.Cash && p.CreatedAt >= from && p.CreatedAt <= to)
            .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var movements = await db.CashMovements.AsNoTracking().Where(m => m.CashShiftId == s.Id).OrderBy(m => m.At).ToListAsync(ct);
        var payIn = movements.Where(m => m.Kind == CashMovementKind.PayIn).Sum(m => m.Amount);
        var payOut = movements.Where(m => m.Kind == CashMovementKind.PayOut).Sum(m => m.Amount);
        var cashSales = byMode.GetValueOrDefault(PaymentMode.Cash);
        var expected = s.OpeningCash + cashSales - cancelledCash + receipts - refunds - supplierCash + payIn - payOut;
        var userName = await db.Users.Where(u => u.Id == uid).Select(u => u.DisplayName).SingleAsync(ct);

        return new ShiftSummary(s.Id, s.ShiftNo, uid, userName, s.Status, s.OpenedAt, s.ClosedAt, billCount, s.OpeningCash, cashSales,
            cancelledCash, receipts, refunds, supplierCash, payIn, payOut, s.Status == ShiftStatus.Closed ? s.ExpectedCash!.Value : expected,
            s.CountedCash, s.Variance, byMode.GetValueOrDefault(PaymentMode.Upi), byMode.GetValueOrDefault(PaymentMode.Card),
            byMode.GetValueOrDefault(PaymentMode.Credit), s.CloseNotes,
            movements.Select(m => new CashMovementDto(m.Kind, m.Amount, m.Reason, m.At)).ToList());
    }
}

/// <summary>Physical stock counts. Counted figures are compared with system stock at the moment of entry.</summary>
public class StockCountService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    SequenceService sequences, AuditService audit)
{
    public async Task<StockCountDto> CreateAsync(CreateCountRequest r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.Title), "Give the count a title (e.g. 'Rack A' or 'Antibiotics').");
        var balances = db.StockBalances.Where(b => b.BranchId == ctx.BranchId && b.QtyUnits > 0);
        if (r.ProductIds is { Count: > 0 }) balances = balances.Where(b => r.ProductIds.Contains(b.ProductId));
        else if (!string.IsNullOrWhiteSpace(r.Search)) balances = balances.Where(b => b.Batch!.Product!.Name.Contains(r.Search.Trim()));
        var rows = await balances.Select(b => new { b.ProductId, b.BatchId, b.Batch!.CostPerUnit }).Take(3001).ToListAsync(ct);
        Require(rows.Count > 0, "No stock found for this selection.");
        Require(rows.Count <= 3000, "Too many batches for one count (max 3000). Narrow the selection.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var count = new StockCount
        {
            BranchId = branch.Id, Title = r.Title.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
            CountNo = SequenceService.Format(branch.Code, "K", today, await sequences.NextAsync(branch.Id, "K", today, ct)),
        };
        count.Lines = rows.Select(x => new StockCountLine { StockCountId = count.Id, ProductId = x.ProductId, BatchId = x.BatchId, CostPerUnit = x.CostPerUnit }).ToList();
        db.StockCounts.Add(count);
        audit.Record("stock_count.created", nameof(StockCount), count.Id, $"{count.CountNo} {count.Title} ({rows.Count} batches)");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(count.Id, ct);
    }

    public async Task<StockCountDto> SaveEntriesAsync(Guid id, List<CountEntry> entries, CancellationToken ct = default)
    {
        var count = await db.StockCounts.Include(c => c.Lines).SingleOrDefaultAsync(c => c.Id == id && c.BranchId == ctx.BranchId, ct)
                    ?? throw BusinessException.NotFound("Stock count");
        if (count.Status != StockCountStatus.InProgress) throw BusinessException.Conflict("not_in_progress", "This count is closed.");
        var batchIds = count.Lines.Select(l => l.BatchId).ToList();
        var now = clock.UtcNow;
        foreach (var e in entries)
        {
            var line = count.Lines.SingleOrDefault(l => l.Id == e.LineId) ?? throw BusinessException.Validation("Line does not belong to this count.");
            if (e.CountedQty is null) { line.CountedQty = null; line.SystemQtyAtCount = null; line.CountedAt = null; continue; }
            Require(e.CountedQty >= 0 && e.CountedQty == decimal.Truncate(e.CountedQty.Value), "Counted quantity must be a whole number ≥ 0.");
            // capture system stock right now, so sales after this moment don't create false variance
            line.SystemQtyAtCount = await db.StockBalances.Where(b => b.BranchId == count.BranchId && b.BatchId == line.BatchId)
                .Select(b => (decimal?)b.QtyUnits).SingleOrDefaultAsync(ct) ?? 0;
            line.CountedQty = e.CountedQty;
            line.CountedAt = now;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<StockCountDto> PostAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var count = await db.StockCounts.Include(c => c.Lines).ThenInclude(l => l.Batch)
                        .SingleOrDefaultAsync(c => c.Id == id && c.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Stock count");
        if (count.Status != StockCountStatus.InProgress) throw BusinessException.Conflict("not_in_progress", "This count is already posted or cancelled.");
        var counted = count.Lines.Where(l => l.CountedQty is not null).ToList();
        Require(counted.Count > 0, "Enter at least one counted quantity before posting.");
        foreach (var l in counted)
        {
            var delta = l.CountedQty!.Value - l.SystemQtyAtCount!.Value;
            if (delta == 0) continue;
            await inventory.ApplyAsync(count.BranchId, l.Batch!, delta, delta > 0 ? MovementType.CountGain : MovementType.CountLoss,
                nameof(StockCount), count.Id, l.Id, $"{count.CountNo} {count.Title}", ct);
        }
        count.Status = StockCountStatus.Posted;
        count.PostedAt = clock.UtcNow;
        count.PostedByUserId = ctx.UserId;
        var gain = counted.Sum(l => Math.Max(0, l.CountedQty!.Value - l.SystemQtyAtCount!.Value) * l.CostPerUnit);
        var loss = counted.Sum(l => Math.Max(0, l.SystemQtyAtCount!.Value - l.CountedQty!.Value) * l.CostPerUnit);
        audit.Record("stock_count.posted", nameof(StockCount), count.Id, $"{count.CountNo} gain {Money.R2(gain)} loss {Money.R2(loss)}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task CancelAsync(Guid id, CancellationToken ct = default)
    {
        var count = await db.StockCounts.SingleOrDefaultAsync(c => c.Id == id && c.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Stock count");
        if (count.Status != StockCountStatus.InProgress) throw BusinessException.Conflict("not_in_progress", "Only in-progress counts can be cancelled.");
        count.Status = StockCountStatus.Cancelled;
        audit.Record("stock_count.cancelled", nameof(StockCount), count.Id, count.CountNo);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StockCountListItem>> ListAsync(CancellationToken ct = default) =>
        await db.StockCounts.AsNoTracking().Where(c => c.BranchId == ctx.BranchId).OrderByDescending(c => c.CreatedAt).Take(100)
            .Select(c => new StockCountListItem(c.Id, c.CountNo, c.Title, c.Status, c.CreatedAt, c.Lines.Count, c.Lines.Count(l => l.CountedQty != null)))
            .ToListAsync(ct);

    public async Task<StockCountDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.StockCounts.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Product).Include(x => x.Lines).ThenInclude(l => l.Batch)
                    .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Stock count");
        var batchIds = c.Lines.Select(l => l.BatchId).ToList();
        var now = await db.StockBalances.AsNoTracking().Where(b => b.BranchId == c.BranchId && batchIds.Contains(b.BatchId))
            .ToDictionaryAsync(b => b.BatchId, b => b.QtyUnits, ct);
        var lines = c.Lines.OrderBy(l => l.Product!.Name).ThenBy(l => l.Batch!.ExpiryDate).Select(l => new CountLineDto(l.Id, l.ProductId, l.Product!.Name,
            l.Product.BaseUnit, l.BatchId, l.Batch!.BatchNo, l.Batch.ExpiryDate, now.GetValueOrDefault(l.BatchId), l.SystemQtyAtCount, l.CountedQty,
            l.CountedQty - l.SystemQtyAtCount, l.CostPerUnit)).ToList();
        var counted = lines.Where(l => l.Variance is not null).ToList();
        return new StockCountDto(c.Id, c.CountNo, c.Title, c.Status, c.CreatedAt, c.PostedAt, lines.Count, counted.Count,
            Money.R2(counted.Where(l => l.Variance > 0).Sum(l => l.Variance!.Value * l.CostPerUnit)),
            Money.R2(counted.Where(l => l.Variance < 0).Sum(l => -l.Variance!.Value * l.CostPerUnit)), lines);
    }
}

/// <summary>Batch quarantine and recall trace (where did this batch come from, who did it go to).</summary>
public class RecallService(PharmaDbContext db, ITenantContext ctx, AuditService audit)
{
    public async Task<List<BatchSearchRow>> SearchAsync(string q, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(q), "Enter a batch number or product name.");
        var s = q.Trim().ToUpperInvariant();
        var batches = await db.Batches.AsNoTracking().Where(b => b.BatchNo.Contains(s) || b.Product!.Name.Contains(q.Trim()))
            .OrderBy(b => b.Product!.Name).ThenBy(b => b.ExpiryDate).Take(50)
            .Select(b => new
            {
                b.Id, b.ProductId, b.Product!.Name, b.Product.Manufacturer, b.BatchNo, b.ExpiryDate, b.Mrp, b.IsBlocked, b.BlockReason,
                Qty = db.StockBalances.Where(x => x.BatchId == b.Id && x.BranchId == ctx.BranchId).Sum(x => (decimal?)x.QtyUnits) ?? 0,
            }).ToListAsync(ct);
        return batches.Select(b => new BatchSearchRow(b.Id, b.ProductId, b.Name, b.Manufacturer, b.BatchNo, b.ExpiryDate, b.Mrp, b.IsBlocked, b.BlockReason, b.Qty)).ToList();
    }

    public async Task<BatchTrace> TraceAsync(Guid batchId, CancellationToken ct = default)
    {
        var row = (await db.Batches.AsNoTracking().Where(b => b.Id == batchId)
            .Select(b => new BatchSearchRow(b.Id, b.ProductId, b.Product!.Name, b.Product.Manufacturer, b.BatchNo, b.ExpiryDate, b.Mrp, b.IsBlocked,
                b.BlockReason, db.StockBalances.Where(x => x.BatchId == b.Id && x.BranchId == ctx.BranchId).Sum(x => (decimal?)x.QtyUnits) ?? 0))
            .SingleOrDefaultAsync(ct)) ?? throw BusinessException.NotFound("Batch");
        var purchases = await db.PurchaseInvoiceLines.AsNoTracking().Where(l => l.BatchId == batchId)
            .Join(db.PurchaseInvoices.Where(p => p.Status == PurchaseStatus.Posted && p.BranchId == ctx.BranchId), l => l.PurchaseInvoiceId, p => p.Id,
                (l, p) => new TracePurchase(p.Id, p.Supplier!.Name, p.SupplierInvoiceNo, p.InvoiceDate, (l.QtyPacks + l.FreeQtyPacks) * l.Product!.PackSize))
            .ToListAsync(ct);
        var sales = await db.SalesInvoiceLines.AsNoTracking().Where(l => l.BatchId == batchId)
            .Join(db.SalesInvoices.Where(s => s.BranchId == ctx.BranchId), l => l.SalesInvoiceId, s => s.Id,
                (l, s) => new TraceSale(s.Id, s.InvoiceNo, s.InvoiceAt, s.CustomerName, s.CustomerPhone, l.QtyUnits, l.ReturnedQtyUnits, s.Status))
            .ToListAsync(ct);
        sales = sales.OrderByDescending(x => x.At).ToList();
        var toSupplier = await db.PurchaseReturnLines.AsNoTracking().Where(l => l.BatchId == batchId)
            .Join(db.PurchaseReturns.Where(r => r.BranchId == ctx.BranchId && (r.Status == PurchaseReturnStatus.Dispatched || r.Status == PurchaseReturnStatus.Settled)),
                l => l.PurchaseReturnId, r => r.Id, (l, r) => l.QtyUnits).SumAsync(x => (decimal?)x, ct) ?? 0;
        return new BatchTrace(row, purchases, sales,
            sales.Where(s => s.Status == SalesStatus.Posted).Sum(s => s.QtyUnits - s.ReturnedQtyUnits), toSupplier);
    }

    public async Task SetBlockedAsync(Guid batchId, bool blocked, string? reason, CancellationToken ct = default)
    {
        if (blocked) Require(!string.IsNullOrWhiteSpace(reason), "A reason is required to block a batch.");
        var b = await db.Batches.Include(x => x.Product).SingleOrDefaultAsync(x => x.Id == batchId, ct) ?? throw BusinessException.NotFound("Batch");
        b.IsBlocked = blocked;
        b.BlockReason = blocked ? reason!.Trim() : null;
        audit.Record(blocked ? "batch.blocked" : "batch.unblocked", nameof(Batch), b.Id, $"{b.Product!.Name} {b.BatchNo}: {reason}");
        await db.SaveChangesAsync(ct);
    }
}
