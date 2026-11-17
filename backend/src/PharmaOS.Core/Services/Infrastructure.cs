using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;

namespace PharmaOS.Core.Services;

/// <summary>Per-branch, per-financial-year document numbers, allocated inside the caller's transaction.</summary>
public class SequenceService(PharmaDbContext db)
{
    public async Task<long> NextAsync(Guid branchId, string docType, DateOnly businessDate, CancellationToken ct = default)
    {
        var fy = BusinessCalendar.FinancialYearCode(businessDate);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            // Atomic increment; the row lock is held until the surrounding transaction commits,
            // so concurrent bills on the same branch serialise here rather than duplicating numbers.
            var updated = await db.DocumentSequences
                .Where(s => s.BranchId == branchId && s.DocType == docType && s.FinancialYear == fy)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextNumber, x => x.NextNumber + 1), ct);

            if (updated == 1)
            {
                var next = await db.DocumentSequences.AsNoTracking()
                    .Where(s => s.BranchId == branchId && s.DocType == docType && s.FinancialYear == fy)
                    .Select(s => s.NextNumber).SingleAsync(ct);
                return next - 1;
            }

            var seq = new DocumentSequence { BranchId = branchId, DocType = docType, FinancialYear = fy, NextNumber = 2 };
            db.DocumentSequences.Add(seq);
            try
            {
                await db.SaveChangesAsync(ct);
                return 1;
            }
            catch (DbUpdateException)
            {
                db.Entry(seq).State = EntityState.Detached; // someone else created it; retry the increment
            }
        }
        throw new InvalidOperationException("Could not allocate document number.");
    }

    public static string Format(string branchCode, string prefix, DateOnly businessDate, long number)
        => $"{prefix}{branchCode}{BusinessCalendar.FinancialYearCode(businessDate)}-{number:D6}";
}

public class AuditService(PharmaDbContext db, ITenantContext ctx, IClock clock)
{
    /// <summary>Adds to the current unit of work so the audit row commits atomically with the change.</summary>
    public void Record(string action, string entityType, Guid? entityId, string? summary = null)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            UserId = ctx.UserId == Guid.Empty ? null : ctx.UserId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Summary = summary is { Length: > 1000 } ? summary[..1000] : summary,
            At = clock.UtcNow,
        });
    }
}

/// <summary>
/// The ONLY place that changes stock. Each call writes an immutable StockMovement and applies the
/// same delta to StockBalance atomically; decrements are conditional so stock can never go negative.
/// </summary>
public class InventoryService(PharmaDbContext db, ITenantContext ctx, IClock clock)
{
    public async Task ApplyAsync(Guid branchId, Batch batch, decimal qtyUnits, MovementType type,
        string sourceType, Guid sourceId, Guid? sourceLineId = null, string? note = null, CancellationToken ct = default)
    {
        if (qtyUnits == 0) return;

        if (qtyUnits < 0)
        {
            var take = -qtyUnits;
            var rows = await db.StockBalances
                .Where(b => b.BranchId == branchId && b.BatchId == batch.Id && b.QtyUnits >= take)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.QtyUnits, x => x.QtyUnits - take), ct);
            if (rows == 0)
                throw BusinessException.Conflict("insufficient_stock",
                    $"Insufficient stock in batch {batch.BatchNo}.");
        }
        else
        {
            var rows = await db.StockBalances
                .Where(b => b.BranchId == branchId && b.BatchId == batch.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.QtyUnits, x => x.QtyUnits + qtyUnits), ct);
            if (rows == 0)
            {
                db.StockBalances.Add(new StockBalance
                {
                    BranchId = branchId, BatchId = batch.Id, ProductId = batch.ProductId, QtyUnits = qtyUnits,
                });
                await db.SaveChangesAsync(ct);
            }
        }

        db.StockMovements.Add(new StockMovement
        {
            BranchId = branchId,
            BatchId = batch.Id,
            ProductId = batch.ProductId,
            QtyUnits = qtyUnits,
            Type = type,
            SourceType = sourceType,
            SourceId = sourceId,
            SourceLineId = sourceLineId,
            Note = note,
            CreatedByUserId = ctx.UserId == Guid.Empty ? null : ctx.UserId,
            CreatedAt = clock.UtcNow,
        });
    }

    /// <summary>Find the batch for (product, batchNo, mrp) or create it. Expiry must match an existing batch.</summary>
    public async Task<Batch> GetOrCreateBatchAsync(Product product, string batchNo, DateOnly expiry, decimal mrp,
        decimal purchaseRate, decimal costPerUnit, CancellationToken ct = default)
    {
        batchNo = batchNo.Trim().ToUpperInvariant();
        expiry = BusinessCalendar.NormalizeExpiry(expiry);
        var batch = await db.Batches.FirstOrDefaultAsync(
            b => b.ProductId == product.Id && b.BatchNo == batchNo && b.Mrp == mrp, ct);
        if (batch is null)
        {
            batch = new Batch
            {
                ProductId = product.Id, BatchNo = batchNo, ExpiryDate = expiry, Mrp = mrp,
                PurchaseRate = purchaseRate, CostPerUnit = costPerUnit, CreatedAt = clock.UtcNow,
            };
            db.Batches.Add(batch);
            await db.SaveChangesAsync(ct);
            return batch;
        }
        if (batch.ExpiryDate != expiry)
            throw BusinessException.Validation(
                $"Batch {batchNo} of {product.Name} already exists with expiry {batch.ExpiryDate:MM/yyyy}.");
        if (purchaseRate > 0)
        {
            batch.PurchaseRate = purchaseRate;
            batch.CostPerUnit = costPerUnit;
        }
        return batch;
    }
}
