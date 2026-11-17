using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record PosBatch(Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal Mrp, decimal QtyUnits);
public record PosProduct(Guid ProductId, string Name, string? Composition, string? Manufacturer, decimal PackSize,
    string PackUnit, string BaseUnit, bool AllowLooseSale, decimal GstRate, DrugSchedule Schedule, decimal AvailableUnits,
    List<PosBatch> Batches, string? Barcode = null, decimal? BranchPricePerPack = null);

public record StockRow(Guid ProductId, string Name, string? Manufacturer, decimal PackSize, string PackUnit, string BaseUnit,
    decimal QtyUnits, decimal ValueAtCost, decimal ReorderLevelUnits, bool BelowReorder, List<StockBatchRow> Batches);
public record StockBatchRow(Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal Mrp, decimal CostPerUnit, decimal QtyUnits, bool IsExpired);

public record AdjustmentLineInput(Guid ProductId, Guid? BatchId, string? BatchNo, DateOnly? ExpiryDate, decimal? Mrp,
    decimal? PurchaseRate, decimal QtyUnits);
public record AdjustmentRequest(bool IsOpening, string Reason, List<AdjustmentLineInput> Lines);
public record AdjustmentResult(Guid Id, string AdjustmentNo, int LineCount);

public record MovementRow(DateTimeOffset At, MovementType Type, string BatchNo, decimal QtyUnits, string SourceType, Guid SourceId, string? Note);

public record NearExpiryRow(Guid ProductId, string ProductName, string? Manufacturer, Guid BatchId, string BatchNo,
    DateOnly ExpiryDate, int DaysToExpiry, decimal QtyUnits, decimal PackSize, decimal ValueAtCost, decimal ValueAtMrp, bool IsExpired);

public record DailySalesReport(DateOnly Date, int InvoiceCount, int CancelledCount, decimal GrossAmount, decimal DiscountAmount,
    decimal TaxableAmount, decimal TaxAmount, decimal NetSales, decimal Returns, decimal NetAfterReturns, decimal CostOfGoods,
    decimal GrossMargin, List<ModeTotal> PaymentsByMode, List<ModeTotal> RefundsByMode);
public record ModeTotal(PaymentMode Mode, decimal Amount);

public class StockService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);

    /// <summary>Counter search: name/composition/barcode prefix with sellable batches in FEFO order.</summary>
    public async Task<List<PosProduct>> PosSearchAsync(string q, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(q)) return [];
        var s = q.Trim();
        var today = Today;
        var products = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && (p.Barcode == s || p.Name.StartsWith(s) || p.Name.Contains(" " + s)
                                       || (p.Composition != null && p.Composition.Contains(s))))
            .OrderBy(p => p.Barcode == s ? 0 : p.Name.StartsWith(s) ? 1 : 2).ThenBy(p => p.Name)
            .Take(20).ToListAsync(ct);
        var ids = products.Select(p => p.Id).ToList();
        var batches = await db.StockBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && ids.Contains(b.ProductId) && b.QtyUnits > 0 && b.Batch!.ExpiryDate >= today && !b.Batch.IsBlocked)
            .OrderBy(b => b.Batch!.ExpiryDate)
            .Select(b => new { b.ProductId, B = new PosBatch(b.BatchId, b.Batch!.BatchNo, b.Batch.ExpiryDate, b.Batch.Mrp, b.QtyUnits) })
            .ToListAsync(ct);
        var prices = await db.BranchProductPrices.AsNoTracking().Where(x => x.BranchId == ctx.BranchId && ids.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, x => (decimal?)x.PricePerPack, ct);
        return products.Select(p =>
        {
            var pb = batches.Where(b => b.ProductId == p.Id).Select(b => b.B).ToList();
            return new PosProduct(p.Id, p.Name, p.Composition, p.Manufacturer, p.PackSize, p.PackUnit, p.BaseUnit,
                p.AllowLooseSale, p.GstRate, p.Schedule, pb.Sum(b => b.QtyUnits), pb, p.Barcode, prices.GetValueOrDefault(p.Id));
        }).ToList();
    }

    /// <summary>
    /// Snapshot of everything sellable at this branch (non-expired, non-blocked, in stock) for the offline POS cache.
    /// Quantities are a snapshot: the server re-checks stock when offline bills sync.
    /// </summary>
    public async Task<List<PosProduct>> PosCatalogAsync(CancellationToken ct = default)
    {
        var today = Today;
        var batches = await db.StockBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.QtyUnits > 0 && b.Batch!.ExpiryDate >= today && !b.Batch.IsBlocked && b.Batch.Product!.IsActive)
            .OrderBy(b => b.Batch!.ExpiryDate)
            .Select(b => new { b.ProductId, B = new PosBatch(b.BatchId, b.Batch!.BatchNo, b.Batch.ExpiryDate, b.Batch.Mrp, b.QtyUnits) })
            .Take(50000).ToListAsync(ct);
        var ids = batches.Select(b => b.ProductId).Distinct().ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        var prices = await db.BranchProductPrices.AsNoTracking().Where(x => x.BranchId == ctx.BranchId)
            .ToDictionaryAsync(x => x.ProductId, x => (decimal?)x.PricePerPack, ct);
        return products.OrderBy(p => p.Name).Select(p =>
        {
            var pb = batches.Where(b => b.ProductId == p.Id).Select(b => b.B).ToList();
            return new PosProduct(p.Id, p.Name, p.Composition, p.Manufacturer, p.PackSize, p.PackUnit, p.BaseUnit, p.AllowLooseSale, p.GstRate,
                p.Schedule, pb.Sum(b => b.QtyUnits), pb, p.Barcode, prices.GetValueOrDefault(p.Id));
        }).ToList();
    }

    public async Task<PagedResult<StockRow>> StockAsync(string? q, bool onlyInStock, bool onlyBelowReorder, int page, int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var today = Today;
        var products = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => p.Name.Contains(q.Trim()) || p.Composition!.Contains(q.Trim()));

        var withQty = products.Select(p => new
        {
            P = p,
            Qty = db.StockBalances.Where(b => b.BranchId == ctx.BranchId && b.ProductId == p.Id).Sum(b => (decimal?)b.QtyUnits) ?? 0,
        });
        if (onlyInStock) withQty = withQty.Where(x => x.Qty > 0);
        if (onlyBelowReorder) withQty = withQty.Where(x => x.Qty <= x.P.ReorderLevelUnits);

        var total = await withQty.CountAsync(ct);
        var pageRows = await withQty.OrderBy(x => x.P.Name).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var ids = pageRows.Select(x => x.P.Id).ToList();
        var batchRows = await db.StockBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && ids.Contains(b.ProductId) && b.QtyUnits != 0)
            .Select(b => new { b.ProductId, b.BatchId, b.Batch!.BatchNo, b.Batch.ExpiryDate, b.Batch.Mrp, b.Batch.CostPerUnit, b.QtyUnits })
            .ToListAsync(ct);

        var items = pageRows.Select(x =>
        {
            var bs = batchRows.Where(b => b.ProductId == x.P.Id).OrderBy(b => b.ExpiryDate)
                .Select(b => new StockBatchRow(b.BatchId, b.BatchNo, b.ExpiryDate, b.Mrp, b.CostPerUnit, b.QtyUnits, b.ExpiryDate < today))
                .ToList();
            return new StockRow(x.P.Id, x.P.Name, x.P.Manufacturer, x.P.PackSize, x.P.PackUnit, x.P.BaseUnit, x.Qty,
                Money.R2(bs.Sum(b => b.QtyUnits * b.CostPerUnit)), x.P.ReorderLevelUnits, x.Qty <= x.P.ReorderLevelUnits, bs);
        }).ToList();
        return new PagedResult<StockRow>(items, total, page, pageSize);
    }

    /// <summary>Opening stock or a correction. Positive lines may create batches; negative lines need an existing batch.</summary>
    public async Task<AdjustmentResult> AdjustAsync(AdjustmentRequest r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.Reason), "Reason is required.");
        Require(r.Lines is { Count: > 0 and <= 1000 }, "Provide 1-1000 lines.");
        if (r.IsOpening) Require(r.Lines.All(l => l.QtyUnits > 0), "Opening stock quantities must be positive.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var today = Today;
        var adj = new StockAdjustment
        {
            BranchId = branch.Id, IsOpening = r.IsOpening, Reason = r.Reason.Trim(), CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
        };
        var number = await sequences.NextAsync(branch.Id, "A", today, ct);
        adj.AdjustmentNo = SequenceService.Format(branch.Code, "A", today, number);
        db.StockAdjustments.Add(adj);
        await db.SaveChangesAsync(ct);

        var i = 0;
        foreach (var l in r.Lines)
        {
            i++;
            Require(l.QtyUnits != 0 && l.QtyUnits == decimal.Truncate(l.QtyUnits), $"Line {i}: quantity must be a non-zero whole number of base units.");
            var product = await db.Products.SingleOrDefaultAsync(p => p.Id == l.ProductId, ct)
                          ?? throw BusinessException.Validation($"Line {i}: product not found.");
            Batch batch;
            if (l.BatchId is not null)
                batch = await db.Batches.SingleOrDefaultAsync(b => b.Id == l.BatchId && b.ProductId == product.Id, ct)
                        ?? throw BusinessException.Validation($"Line {i}: batch not found for {product.Name}.");
            else
            {
                Require(!string.IsNullOrWhiteSpace(l.BatchNo) && l.ExpiryDate is not null && l.Mrp is > 0,
                    $"Line {i}: batch no, expiry and MRP are required for a new batch.");
                var rate = l.PurchaseRate ?? 0;
                batch = await inventory.GetOrCreateBatchAsync(product, l.BatchNo!, l.ExpiryDate!.Value, l.Mrp!.Value, rate,
                    Money.R4(rate / product.PackSize), ct);
            }

            var line = new StockAdjustmentLine { StockAdjustmentId = adj.Id, ProductId = product.Id, BatchId = batch.Id, QtyUnits = l.QtyUnits };
            adj.Lines.Add(line);
            var type = r.IsOpening ? MovementType.Opening : l.QtyUnits > 0 ? MovementType.AdjustmentIn : MovementType.AdjustmentOut;
            await inventory.ApplyAsync(branch.Id, batch, l.QtyUnits, type, nameof(StockAdjustment), adj.Id, line.Id, r.Reason, ct);
        }
        audit.Record(r.IsOpening ? "stock.opening" : "stock.adjusted", nameof(StockAdjustment), adj.Id, $"{adj.AdjustmentNo}: {r.Reason}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new AdjustmentResult(adj.Id, adj.AdjustmentNo, adj.Lines.Count);
    }

    public async Task<List<MovementRow>> MovementsAsync(Guid productId, int take, CancellationToken ct = default) =>
        await db.StockMovements.AsNoTracking()
            .Where(m => m.BranchId == ctx.BranchId && m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAt).Take(Math.Clamp(take, 1, 500))
            .Join(db.Batches, m => m.BatchId, b => b.Id, (m, b) => new MovementRow(m.CreatedAt, m.Type, b.BatchNo, m.QtyUnits, m.SourceType, m.SourceId, m.Note))
            .ToListAsync(ct);

    /// <summary>Batches with stock expiring within N days (includes already-expired stock still on hand).</summary>
    public async Task<List<NearExpiryRow>> NearExpiryAsync(int days, CancellationToken ct = default)
    {
        var today = Today;
        var until = today.AddDays(Math.Clamp(days, 0, 730));
        var rows = await db.StockBalances.AsNoTracking()
            .Where(b => b.BranchId == ctx.BranchId && b.QtyUnits > 0 && b.Batch!.ExpiryDate <= until)
            .Select(b => new
            {
                b.ProductId, b.Batch!.Product!.Name, b.Batch.Product.Manufacturer, b.Batch.Product.PackSize, b.BatchId,
                b.Batch.BatchNo, b.Batch.ExpiryDate, b.Batch.Mrp, b.Batch.CostPerUnit, b.QtyUnits,
            })
            .OrderBy(x => x.ExpiryDate).ToListAsync(ct);
        return rows.Select(x => new NearExpiryRow(x.ProductId, x.Name, x.Manufacturer, x.BatchId, x.BatchNo, x.ExpiryDate,
            x.ExpiryDate.DayNumber - today.DayNumber, x.QtyUnits, x.PackSize, Money.R2(x.QtyUnits * x.CostPerUnit),
            Money.R2(x.QtyUnits * x.Mrp / x.PackSize), x.ExpiryDate < today)).ToList();
    }

    /// <summary>
    /// Daily sales (business date, branch). NetSales = sum of posted (non-cancelled) invoice net amounts.
    /// Returns are counted on the return's business date.
    /// GrossMargin = (sales taxable value − returns taxable value) − (cost of units sold − cost of units returned),
    /// all excluding GST; cost = batch cost-per-unit captured on the sale line. Round-off is excluded.
    /// </summary>
    public async Task<DailySalesReport> DailySalesAsync(DateOnly date, CancellationToken ct = default)
    {
        var inv = db.SalesInvoices.AsNoTracking().Where(s => s.BranchId == ctx.BranchId && s.BusinessDate == date);
        var posted = inv.Where(s => s.Status == SalesStatus.Posted);
        var count = await posted.CountAsync(ct);
        var cancelled = await inv.CountAsync(s => s.Status == SalesStatus.Cancelled, ct);
        var totals = await posted.GroupBy(_ => 1).Select(g => new
        {
            Gross = g.Sum(x => x.GrossAmount), Disc = g.Sum(x => x.DiscountAmount), Taxable = g.Sum(x => x.TaxableAmount),
            Tax = g.Sum(x => x.TaxAmount), Net = g.Sum(x => x.NetAmount),
        }).SingleOrDefaultAsync(ct);
        var cogsSold = await db.SalesInvoiceLines.AsNoTracking()
            .Where(l => posted.Select(p => p.Id).Contains(l.SalesInvoiceId))
            .SumAsync(l => (decimal?)(l.QtyUnits * l.CostPerUnit), ct) ?? 0;
        var pay = await db.SalesPayments.AsNoTracking().Where(p => posted.Select(x => x.Id).Contains(p.SalesInvoiceId))
            .GroupBy(p => p.Mode).Select(g => new ModeTotal(g.Key, g.Sum(x => x.Amount))).ToListAsync(ct);

        var returns = db.SalesReturns.AsNoTracking().Where(r => r.BranchId == ctx.BranchId && r.BusinessDate == date);
        var retTotal = await returns.SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0;
        var refunds = await returns.GroupBy(r => r.RefundMode).Select(g => new ModeTotal(g.Key, g.Sum(x => x.RefundAmount))).ToListAsync(ct);

        // Returns on this date: reverse their revenue (ex-GST) and their cost, using the original line's rate and cost.
        var retLines = await db.SalesReturnLines.AsNoTracking()
            .Where(rl => returns.Select(r => r.Id).Contains(rl.SalesReturnId))
            .Join(db.SalesInvoiceLines, rl => rl.SalesInvoiceLineId, il => il.Id,
                (rl, il) => new { rl.Amount, rl.QtyUnits, il.GstRate, il.CostPerUnit })
            .ToListAsync(ct);
        var retTaxable = retLines.Sum(x => Money.SplitInclusive(x.Amount, x.GstRate).Taxable);
        var retCost = retLines.Sum(x => x.QtyUnits * x.CostPerUnit);

        var net = totals?.Net ?? 0;
        var taxable = totals?.Taxable ?? 0;
        var cogs = Money.R2(cogsSold - retCost);
        return new DailySalesReport(date, count, cancelled, totals?.Gross ?? 0, totals?.Disc ?? 0, taxable, totals?.Tax ?? 0,
            net, retTotal, net - retTotal, cogs, Money.R2(taxable - retTaxable - cogs), pay, refunds);
    }
}
