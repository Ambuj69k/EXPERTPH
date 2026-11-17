using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record PurchaseLineInput(Guid ProductId, string BatchNo, DateOnly ExpiryDate, decimal Mrp, decimal Rate,
    decimal QtyPacks, decimal FreeQtyPacks, decimal DiscountPercent, decimal? GstRate);

public record PurchaseInput(Guid SupplierId, string SupplierInvoiceNo, DateOnly InvoiceDate, decimal OtherCharges,
    string? Notes, List<PurchaseLineInput> Lines, int? Version, Guid? PurchaseOrderId = null);

public record PurchaseLineDto(Guid Id, int LineNo, Guid ProductId, string ProductName, decimal PackSize, string BatchNo,
    DateOnly ExpiryDate, decimal Mrp, decimal Rate, decimal QtyPacks, decimal FreeQtyPacks, decimal DiscountPercent,
    decimal GstRate, decimal TaxableAmount, decimal TaxAmount, decimal LineTotal);

public record PurchaseDto(Guid Id, Guid SupplierId, string SupplierName, string SupplierInvoiceNo, DateOnly InvoiceDate,
    PurchaseStatus Status, decimal GrossAmount, decimal DiscountAmount, decimal TaxableAmount, decimal TaxAmount,
    decimal OtherCharges, decimal RoundOff, decimal NetAmount, string? Notes, DateTimeOffset CreatedAt,
    DateTimeOffset? PostedAt, string? CancelReason, int Version, List<PurchaseLineDto> Lines, Guid? PurchaseOrderId = null);

public record PurchaseListItem(Guid Id, string SupplierName, string SupplierInvoiceNo, DateOnly InvoiceDate,
    PurchaseStatus Status, decimal NetAmount, int LineCount);

public class PurchaseService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory, AuditService audit,
    PurchasingService purchasing)
{
    public async Task<PagedResult<PurchaseListItem>> ListAsync(PurchaseStatus? status, Guid? supplierId, DateOnly? from,
        DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.PurchaseInvoices.AsNoTracking().Where(p => p.BranchId == ctx.BranchId);
        if (status is not null) q = q.Where(p => p.Status == status);
        if (supplierId is not null) q = q.Where(p => p.SupplierId == supplierId);
        if (from is not null) q = q.Where(p => p.InvoiceDate >= from);
        if (to is not null) q = q.Where(p => p.InvoiceDate <= to);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(p => p.InvoiceDate).ThenByDescending(p => p.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => new PurchaseListItem(p.Id, p.Supplier!.Name, p.SupplierInvoiceNo, p.InvoiceDate, p.Status,
                p.NetAmount, p.Lines.Count))
            .ToListAsync(ct);
        return new PagedResult<PurchaseListItem>(items, total, page, pageSize);
    }

    public async Task<PurchaseDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await LoadAsync(id, tracking: false, ct);
        return ToDto(p);
    }

    public async Task<PurchaseDto> CreateDraftAsync(PurchaseInput input, CancellationToken ct = default)
    {
        var supplier = await ValidateHeaderAsync(input, null, ct);
        var p = new PurchaseInvoice
        {
            BranchId = ctx.BranchId, CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId, Supplier = supplier,
        };
        await ApplyAsync(p, input, ct);
        db.PurchaseInvoices.Add(p);
        audit.Record("purchase.draft_created", nameof(PurchaseInvoice), p.Id, $"{supplier.Name} #{p.SupplierInvoiceNo}");
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<PurchaseDto> UpdateDraftAsync(Guid id, PurchaseInput input, CancellationToken ct = default)
    {
        var p = await LoadAsync(id, tracking: true, ct);
        if (p.Status != PurchaseStatus.Draft) throw BusinessException.Conflict("not_draft", "Only draft purchases can be edited.");
        if (input.Version is null) throw BusinessException.Validation("Version is required for updates.");
        db.Entry(p).Property(x => x.Version).OriginalValue = input.Version.Value;
        p.Supplier = await ValidateHeaderAsync(input, id, ct);

        db.PurchaseInvoiceLines.RemoveRange(p.Lines);
        p.Lines = [];
        await ApplyAsync(p, input, ct);
        audit.Record("purchase.draft_updated", nameof(PurchaseInvoice), p.Id, p.SupplierInvoiceNo);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw BusinessException.Conflict("concurrency", "This purchase was changed by someone else. Reload and try again.");
        }
        return ToDto(p);
    }

    public async Task DeleteDraftAsync(Guid id, CancellationToken ct = default)
    {
        var p = await LoadAsync(id, tracking: true, ct);
        if (p.Status != PurchaseStatus.Draft) throw BusinessException.Conflict("not_draft", "Only drafts can be deleted. Cancel posted purchases instead.");
        db.PurchaseInvoiceLines.RemoveRange(p.Lines);
        db.PurchaseInvoices.Remove(p);
        audit.Record("purchase.draft_deleted", nameof(PurchaseInvoice), p.Id, p.SupplierInvoiceNo);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Posting creates/updates batches and receives stock (paid + free qty). After this the document is read-only.</summary>
    public async Task<PurchaseDto> PostAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await LoadAsync(id, tracking: true, ct);
        if (p.Status != PurchaseStatus.Draft) throw BusinessException.Conflict("not_draft", "This purchase is already posted or cancelled.");
        if (p.Lines.Count == 0) throw BusinessException.Validation("Add at least one line before posting.");

        foreach (var line in p.Lines.OrderBy(l => l.LineNo))
        {
            var product = line.Product!;
            var units = (line.QtyPacks + line.FreeQtyPacks) * product.PackSize;
            var costPerUnit = units == 0 ? 0 : Money.R4(line.TaxableAmount / units);
            var batch = await inventory.GetOrCreateBatchAsync(product, line.BatchNo, line.ExpiryDate, line.Mrp, line.Rate, costPerUnit, ct);
            line.BatchId = batch.Id;
            await inventory.ApplyAsync(p.BranchId, batch, units, MovementType.PurchaseReceipt,
                nameof(PurchaseInvoice), p.Id, line.Id, $"{p.Supplier!.Name} #{p.SupplierInvoiceNo}", ct);
        }

        await purchasing.ApplyReceiptAsync(p, +1, ct);
        p.Status = PurchaseStatus.Posted;
        p.PostedAt = clock.UtcNow;
        p.PostedByUserId = ctx.UserId;
        audit.Record("purchase.posted", nameof(PurchaseInvoice), p.Id, $"{p.SupplierInvoiceNo} net {p.NetAmount}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(p);
    }

    /// <summary>Reverses a posted purchase. Only possible while all received stock is still on hand.</summary>
    public async Task<PurchaseDto> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "A cancellation reason is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var p = await LoadAsync(id, tracking: true, ct);
        if (p.Status != PurchaseStatus.Posted) throw BusinessException.Conflict("not_posted", "Only posted purchases can be cancelled.");
        var paid = await db.SupplierPaymentAllocations.AnyAsync(a => a.PurchaseInvoiceId == p.Id
            && db.SupplierPayments.Any(sp => sp.Id == a.SupplierPaymentId && sp.Status == DocStatus.Posted), ct);
        if (paid) throw BusinessException.Conflict("has_payments", "Payments are allocated to this purchase. Cancel those payments first.");

        foreach (var line in p.Lines)
        {
            var batch = await db.Batches.SingleAsync(b => b.Id == line.BatchId, ct);
            var units = (line.QtyPacks + line.FreeQtyPacks) * line.Product!.PackSize;
            try
            {
                await inventory.ApplyAsync(p.BranchId, batch, -units, MovementType.PurchaseCancel,
                    nameof(PurchaseInvoice), p.Id, line.Id, reason, ct);
            }
            catch (BusinessException e) when (e.Code == "insufficient_stock")
            {
                throw BusinessException.Conflict("stock_consumed",
                    $"Cannot cancel: stock of {line.Product.Name} batch {line.BatchNo} has already been sold or adjusted. Use a purchase return instead.");
            }
        }
        await purchasing.ApplyReceiptAsync(p, -1, ct);
        p.Status = PurchaseStatus.Cancelled;
        p.CancelledAt = clock.UtcNow;
        p.CancelReason = reason.Trim();
        audit.Record("purchase.cancelled", nameof(PurchaseInvoice), p.Id, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(p);
    }

    // ---------- internals ----------

    private async Task<Supplier> ValidateHeaderAsync(PurchaseInput input, Guid? exceptId, CancellationToken ct)
    {
        Require(!string.IsNullOrWhiteSpace(input.SupplierInvoiceNo) && input.SupplierInvoiceNo.Length <= 50, "Supplier invoice number is required.");
        Require(input.OtherCharges >= 0, "Other charges cannot be negative.");
        Require(input.Lines is { Count: > 0 and <= 500 }, "A purchase needs 1-500 lines.");
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        Require(input.InvoiceDate <= today, "Invoice date cannot be in the future.");

        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == input.SupplierId, ct)
                       ?? throw BusinessException.NotFound("Supplier");
        var invNo = input.SupplierInvoiceNo.Trim().ToUpperInvariant();
        if (await db.PurchaseInvoices.AnyAsync(p => p.SupplierId == supplier.Id && p.SupplierInvoiceNo == invNo && p.Id != exceptId
                                                   && p.Status != PurchaseStatus.Cancelled, ct))
            throw BusinessException.Conflict("duplicate_invoice", $"Invoice {invNo} from {supplier.Name} is already entered.");
        await purchasing.ValidateOrderForInvoiceAsync(input.PurchaseOrderId, supplier.Id, ct);
        return supplier;
    }

    private async Task ApplyAsync(PurchaseInvoice p, PurchaseInput input, CancellationToken ct)
    {
        var productIds = input.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow);

        p.SupplierId = input.SupplierId;
        p.PurchaseOrderId = input.PurchaseOrderId;
        p.SupplierInvoiceNo = input.SupplierInvoiceNo.Trim().ToUpperInvariant();
        p.InvoiceDate = input.InvoiceDate;
        p.OtherCharges = Money.R2(input.OtherCharges);
        p.Notes = input.Notes?.Trim();

        var lineNo = 0;
        foreach (var l in input.Lines)
        {
            lineNo++;
            if (!products.TryGetValue(l.ProductId, out var product)) throw BusinessException.Validation($"Line {lineNo}: product not found.");
            Require(!string.IsNullOrWhiteSpace(l.BatchNo) && l.BatchNo.Length <= 50, $"Line {lineNo}: batch number is required.");
            var expiry = BusinessCalendar.NormalizeExpiry(l.ExpiryDate);
            Require(expiry >= today, $"Line {lineNo}: batch {l.BatchNo} is already expired.");
            Require(l.QtyPacks > 0 || l.FreeQtyPacks > 0, $"Line {lineNo}: quantity is required.");
            Require(l.QtyPacks >= 0 && l.FreeQtyPacks >= 0, $"Line {lineNo}: quantities cannot be negative.");
            Require(l.Mrp > 0, $"Line {lineNo}: MRP is required.");
            Require(l.Rate >= 0, $"Line {lineNo}: rate cannot be negative.");
            Require(l.DiscountPercent is >= 0 and <= 100, $"Line {lineNo}: discount must be 0-100%.");
            var gst = l.GstRate ?? product.GstRate;
            Require(gst is >= 0 and <= 28, $"Line {lineNo}: invalid GST rate.");

            // Purchase price per unit should not exceed MRP per unit after tax — common data-entry error.
            var unitLanded = l.Rate * (1 - l.DiscountPercent / 100m) * (1 + gst / 100m);
            Require(unitLanded <= l.Mrp * 1.0001m, $"Line {lineNo}: rate incl. GST exceeds MRP. Check rate/MRP.");

            var gross = Money.R2(l.Rate * l.QtyPacks);
            var discount = Money.R2(gross * l.DiscountPercent / 100m);
            var taxable = gross - discount;
            var tax = Money.TaxOnExclusive(taxable, gst);
            p.Lines.Add(new PurchaseInvoiceLine
            {
                PurchaseInvoiceId = p.Id, LineNo = lineNo, ProductId = product.Id, Product = product,
                BatchNo = l.BatchNo.Trim().ToUpperInvariant(), ExpiryDate = expiry, Mrp = l.Mrp, Rate = l.Rate,
                QtyPacks = l.QtyPacks, FreeQtyPacks = l.FreeQtyPacks, DiscountPercent = l.DiscountPercent, GstRate = gst,
                GrossAmount = gross, DiscountAmount = discount, TaxableAmount = taxable, TaxAmount = tax,
                LineTotal = taxable + tax,
            });
        }

        p.GrossAmount = p.Lines.Sum(x => x.GrossAmount);
        p.DiscountAmount = p.Lines.Sum(x => x.DiscountAmount);
        p.TaxableAmount = p.Lines.Sum(x => x.TaxableAmount);
        p.TaxAmount = p.Lines.Sum(x => x.TaxAmount);
        var (rounded, roundOff) = Money.RoundToRupee(p.TaxableAmount + p.TaxAmount + p.OtherCharges);
        p.RoundOff = roundOff;
        p.NetAmount = rounded;
    }

    private async Task<PurchaseInvoice> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var q = db.PurchaseInvoices.Include(p => p.Supplier).Include(p => p.Lines).ThenInclude(l => l.Product)
            .Where(p => p.Id == id && p.BranchId == ctx.BranchId);
        if (!tracking) q = q.AsNoTracking();
        return await q.SingleOrDefaultAsync(ct) ?? throw BusinessException.NotFound("Purchase");
    }

    private static PurchaseDto ToDto(PurchaseInvoice p) => new(p.Id, p.SupplierId, p.Supplier?.Name ?? "", p.SupplierInvoiceNo,
        p.InvoiceDate, p.Status, p.GrossAmount, p.DiscountAmount, p.TaxableAmount, p.TaxAmount, p.OtherCharges, p.RoundOff,
        p.NetAmount, p.Notes, p.CreatedAt, p.PostedAt, p.CancelReason, p.Version, PurchaseOrderId: p.PurchaseOrderId, Lines:
        p.Lines.OrderBy(l => l.LineNo).Select(l => new PurchaseLineDto(l.Id, l.LineNo, l.ProductId, l.Product?.Name ?? "",
            l.Product?.PackSize ?? 1, l.BatchNo, l.ExpiryDate, l.Mrp, l.Rate, l.QtyPacks, l.FreeQtyPacks, l.DiscountPercent,
            l.GstRate, l.TaxableAmount, l.TaxAmount, l.LineTotal)).ToList());
}
