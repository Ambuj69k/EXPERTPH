using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------
public record RetailerInput(string Name, string? Gstin, string StateCode, string? DrugLicenseNo, DateOnly? DrugLicenseValidTo, string? Phone,
    string? Address, string? Route, decimal CreditLimit, int CreditDays, Guid? SalesmanUserId, bool IsActive, int? Version);
public record RetailerDto(Guid Id, string Name, string? Gstin, string StateCode, string? DrugLicenseNo, DateOnly? DrugLicenseValidTo, bool LicenceExpired,
    string? Phone, string? Address, string? Route, decimal CreditLimit, int CreditDays, Guid? SalesmanUserId, string? SalesmanName, bool IsActive,
    int Version, decimal Outstanding, decimal Overdue);
public record TradeRateInput(Guid ProductId, Guid? RetailerId, decimal? RatePerPack);
public record TradeRateRow(Guid ProductId, string ProductName, decimal PackSize, decimal GstRate, decimal? DefaultRate, decimal? RetailerRate);
public record SchemeInput(Guid ProductId, string Name, decimal BuyQtyPacks, decimal FreeQtyPacks, decimal DiscountPercent, DateOnly ValidFrom, DateOnly ValidTo, bool IsActive);
public record SchemeDto(Guid Id, Guid ProductId, string ProductName, string Name, decimal BuyQtyPacks, decimal FreeQtyPacks, decimal DiscountPercent,
    DateOnly ValidFrom, DateOnly ValidTo, bool IsActive);
public record OrderLineInput(Guid ProductId, decimal QtyPacks);
public record SalesOrderInput(Guid RetailerId, string? Notes, List<OrderLineInput> Lines);
public record SalesOrderDto(Guid Id, string OrderNo, Guid RetailerId, string RetailerName, SalesOrderStatus Status, string? Notes, string? TakenBy,
    DateTimeOffset CreatedAt, Guid? TradeInvoiceId, List<SalesOrderLineDto> Lines);
public record SalesOrderLineDto(Guid ProductId, string ProductName, decimal QtyPacks, decimal AvailablePacks);
public record TradeLineInput(Guid ProductId, decimal QtyPacks, decimal DiscountPercent = 0);
public record TradeInvoiceRequest(Guid ClientRequestId, Guid RetailerId, List<TradeLineInput> Lines, Guid? SalesOrderId = null, string? CreditOverrideReason = null);
public record TradeLineDto(Guid Id, int LineNo, Guid ProductId, string ProductName, string? HsnCode, string BatchNo, DateOnly ExpiryDate, decimal QtyPacks,
    decimal FreeQtyPacks, decimal RatePerPack, decimal DiscountPercent, decimal TaxableAmount, decimal GstRate, decimal TaxAmount, decimal LineTotal, decimal ReturnedPacks);
public record TradeInvoiceDto(Guid Id, string InvoiceNo, DateOnly InvoiceDate, DateOnly DueDate, TradeDocStatus Status, Guid RetailerId, string RetailerName,
    string? RetailerGstin, string? RetailerLicence, bool InterState, decimal GrossAmount, decimal DiscountAmount, decimal TaxableAmount, decimal TaxAmount,
    decimal RoundOff, decimal NetAmount, decimal Outstanding, string? CancelReason, List<TradeLineDto> Lines, PrintHeader Seller);
public record TradeInvoiceListItem(Guid Id, string InvoiceNo, DateOnly InvoiceDate, DateOnly DueDate, string RetailerName, TradeDocStatus Status, decimal NetAmount, decimal Outstanding);
public record CreditNoteLineInput(Guid TradeInvoiceLineId, decimal QtyPacks);
public record CreditNoteRequest(Guid ClientRequestId, Guid TradeInvoiceId, TradeReturnReason Reason, string? Notes, List<CreditNoteLineInput> Lines);
public record CreditNoteDto(Guid Id, string CreditNoteNo, DateOnly Date, string InvoiceNo, TradeReturnReason Reason, decimal TaxableAmount, decimal TaxAmount, decimal Amount);
public record TradeReceiptRequest(Guid RetailerId, SettlementMode Mode, decimal Amount, string? Reference, List<AllocationInput>? Allocations);
public record TradeReceiptDto(Guid Id, string ReceiptNo, DateOnly Date, Guid RetailerId, string RetailerName, SettlementMode Mode, decimal Amount, decimal Allocated,
    string? Reference, TradeDocStatus Status, string? CollectedBy);
public record RetailerStatement(Guid RetailerId, string Name, decimal Outstanding, decimal Overdue, decimal CreditLimit, decimal AvailableCredit,
    List<OpenDocument> OpenInvoices, List<StatementLine> Lines);
public record TradeAging(List<AgingBucket> Buckets, decimal Total, List<RetailerDto> TopOverdue);
public record SalesmanRow(Guid? UserId, string Name, int Invoices, decimal Sales, decimal Collections, int Orders);

/// <summary>
/// B2B distribution. Pricing: retailer-specific trade rate (PTR, ex-GST per pack) else the default rate.
/// Schemes add free packs (floor(qty/buy) × free) and/or a trade discount. GST is IGST when the retailer's state differs
/// from the branch's, otherwise CGST+SGST — rules (place of supply, e-invoicing, e-way bills) must be confirmed by a CA.
/// Credit control blocks invoices over the limit or with overdue bills unless the owner overrides with a reason.
/// </summary>
public partial class DistributorService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);
    private bool IsSalesman => ctx.Role == UserRole.Salesman;

    private IQueryable<Retailer> Visible() => IsSalesman ? db.Retailers.Where(r => r.SalesmanUserId == ctx.UserId) : db.Retailers;

    // ===================== retailers =====================

    public async Task<List<RetailerDto>> RetailersAsync(string? q, CancellationToken ct = default)
    {
        var query = Visible().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(r => r.Name.Contains(q.Trim()) || r.Gstin == q.Trim() || r.Route == q.Trim());
        var list = await query.OrderBy(r => r.Name).Take(300).ToListAsync(ct);
        return await ToDtos(list, ct);
    }

    public async Task<RetailerDto> SaveRetailerAsync(Guid? id, RetailerInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name), "Retailer name is required.");
        Require(StateRx().IsMatch(i.StateCode ?? ""), "State code must be 2 digits.");
        var gstin = string.IsNullOrWhiteSpace(i.Gstin) ? null : i.Gstin.Trim().ToUpperInvariant();
        if (gstin is not null)
        {
            Require(GstinRx().IsMatch(gstin), "GSTIN format looks invalid.");
            Require(gstin[..2] == i.StateCode, "GSTIN's first two digits must match the state code.");
        }
        Require(i.CreditLimit >= 0 && i.CreditDays is >= 0 and <= 365, "Credit limit/days invalid.");
        if (i.SalesmanUserId is { } sm && !await db.Users.AnyAsync(u => u.Id == sm && u.Role == UserRole.Salesman && u.IsActive, ct))
            throw BusinessException.Validation("Choose an active salesman.");
        if (gstin is not null && await db.Retailers.AnyAsync(r => r.Gstin == gstin && r.Id != id, ct))
            throw BusinessException.Conflict("duplicate_gstin", "Another retailer has this GSTIN.");
        Retailer r;
        if (id is null) { r = new Retailer(); db.Retailers.Add(r); }
        else
        {
            r = await db.Retailers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Retailer");
            Require(i.Version is not null, "Version is required.");
            db.Entry(r).Property(x => x.Version).OriginalValue = i.Version!.Value;
        }
        r.Name = i.Name.Trim(); r.Gstin = gstin; r.StateCode = i.StateCode; r.DrugLicenseNo = i.DrugLicenseNo?.Trim(); r.DrugLicenseValidTo = i.DrugLicenseValidTo;
        r.Phone = i.Phone?.Trim(); r.Address = i.Address?.Trim(); r.Route = i.Route?.Trim(); r.CreditLimit = Money.R2(i.CreditLimit); r.CreditDays = i.CreditDays;
        r.SalesmanUserId = i.SalesmanUserId; r.IsActive = i.IsActive;
        audit.Record(id is null ? "retailer.created" : "retailer.updated", nameof(Retailer), r.Id, $"{r.Name} limit {r.CreditLimit}");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "Changed by someone else. Reload."); }
        return (await ToDtos([r], ct)).Single();
    }

    private async Task<List<RetailerDto>> ToDtos(List<Retailer> list, CancellationToken ct)
    {
        var ids = list.Select(r => r.Id).ToList();
        var open = (await OpenInvoicesAsync(ids, ct)).GroupBy(o => o.RetailerId).ToDictionary(g => g.Key, g => g.ToList());
        var unallocated = await UnallocatedAsync(ids, ct);
        var smIds = list.Where(r => r.SalesmanUserId != null).Select(r => r.SalesmanUserId!.Value).Distinct().ToList();
        var names = await db.Users.Where(u => smIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var today = Today;
        return list.Select(r =>
        {
            var docs = open.GetValueOrDefault(r.Id) ?? [];
            var outstanding = docs.Sum(d => d.Doc.Outstanding) - unallocated.GetValueOrDefault(r.Id);
            return new RetailerDto(r.Id, r.Name, r.Gstin, r.StateCode, r.DrugLicenseNo, r.DrugLicenseValidTo, r.DrugLicenseValidTo < today, r.Phone, r.Address, r.Route,
                r.CreditLimit, r.CreditDays, r.SalesmanUserId, r.SalesmanUserId is { } s ? names.GetValueOrDefault(s) : null, r.IsActive, r.Version,
                outstanding, docs.Where(d => d.Doc.DaysOverdue > 0).Sum(d => d.Doc.Outstanding));
        }).ToList();
    }

    // ===================== trade rates & schemes =====================

    public async Task<List<TradeRateRow>> RatesAsync(string? q, Guid? retailerId, CancellationToken ct = default)
    {
        var products = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => p.Name.Contains(q.Trim()));
        return await products.OrderBy(p => p.Name).Take(300).Select(p => new TradeRateRow(p.Id, p.Name, p.PackSize, p.GstRate,
            db.TradeRates.Where(t => t.ProductId == p.Id && t.RetailerId == null).Select(t => (decimal?)t.RatePerPack).FirstOrDefault(),
            retailerId == null ? null : db.TradeRates.Where(t => t.ProductId == p.Id && t.RetailerId == retailerId).Select(t => (decimal?)t.RatePerPack).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task SetRateAsync(TradeRateInput i, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == i.ProductId, ct)) throw BusinessException.NotFound("Product");
        if (i.RetailerId is { } rid && !await db.Retailers.AnyAsync(r => r.Id == rid, ct)) throw BusinessException.NotFound("Retailer");
        var t = await db.TradeRates.SingleOrDefaultAsync(x => x.ProductId == i.ProductId && x.RetailerId == i.RetailerId, ct);
        if (i.RatePerPack is null) { if (t is not null) db.TradeRates.Remove(t); }
        else
        {
            Require(i.RatePerPack > 0, "Rate must be positive.");
            t ??= db.TradeRates.Add(new TradeRate { ProductId = i.ProductId, RetailerId = i.RetailerId }).Entity;
            t.RatePerPack = Money.R2(i.RatePerPack.Value);
            t.UpdatedAt = clock.UtcNow;
        }
        audit.Record("trade_rate.set", nameof(Product), i.ProductId, $"{i.RatePerPack?.ToString() ?? "removed"}{(i.RetailerId is null ? " (default)" : " for retailer")}");
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<SchemeDto>> SchemesAsync(CancellationToken ct = default) =>
        await db.TradeSchemes.AsNoTracking().OrderByDescending(s => s.ValidTo)
            .Select(s => new SchemeDto(s.Id, s.ProductId, s.Product!.Name, s.Name, s.BuyQtyPacks, s.FreeQtyPacks, s.DiscountPercent, s.ValidFrom, s.ValidTo, s.IsActive))
            .ToListAsync(ct);

    public async Task<SchemeDto> SaveSchemeAsync(Guid? id, SchemeInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name), "Scheme name is required.");
        Require(i.ValidFrom <= i.ValidTo, "Valid-from must be before valid-to.");
        Require(i.DiscountPercent is >= 0 and < 100, "Discount must be 0-99%.");
        Require((i.BuyQtyPacks > 0 && i.FreeQtyPacks > 0 && i.FreeQtyPacks == decimal.Truncate(i.FreeQtyPacks) && i.BuyQtyPacks == decimal.Truncate(i.BuyQtyPacks))
                || (i.BuyQtyPacks == 0 && i.FreeQtyPacks == 0 && i.DiscountPercent > 0), "Give whole buy + free packs (e.g. 10 + 1) and/or a discount.");
        if (!await db.Products.AnyAsync(p => p.Id == i.ProductId, ct)) throw BusinessException.NotFound("Product");
        TradeScheme s;
        if (id is null) { s = new TradeScheme(); db.TradeSchemes.Add(s); }
        else s = await db.TradeSchemes.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Scheme");
        s.ProductId = i.ProductId; s.Name = i.Name.Trim(); s.BuyQtyPacks = i.BuyQtyPacks; s.FreeQtyPacks = i.FreeQtyPacks; s.DiscountPercent = i.DiscountPercent;
        s.ValidFrom = i.ValidFrom; s.ValidTo = i.ValidTo; s.IsActive = i.IsActive;
        audit.Record("trade_scheme.saved", nameof(TradeScheme), s.Id, s.Name);
        await db.SaveChangesAsync(ct);
        return (await SchemesAsync(ct)).Single(x => x.Id == s.Id);
    }

    // ===================== orders =====================

    public async Task<SalesOrderDto> CreateOrderAsync(SalesOrderInput r, CancellationToken ct = default)
    {
        var retailer = await Visible().SingleOrDefaultAsync(x => x.Id == r.RetailerId && x.IsActive, ct) ?? throw BusinessException.NotFound("Retailer");
        Require(r.Lines is { Count: > 0 and <= 300 }, "An order needs 1-300 lines.");
        Require(r.Lines.All(l => l.QtyPacks > 0 && l.QtyPacks == decimal.Truncate(l.QtyPacks)), "Quantities must be whole packs.");
        var ids = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        Require(ids.Count == r.Lines.Count, "Each product once per order.");
        Require(await db.Products.CountAsync(p => ids.Contains(p.Id) && p.IsActive, ct) == ids.Count, "Unknown product on order.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var o = new SalesOrder
        {
            BranchId = branch.Id, RetailerId = retailer.Id, Notes = r.Notes?.Trim(), TakenByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            OrderNo = SequenceService.Format(branch.Code, "Q", Today, await sequences.NextAsync(branch.Id, "Q", Today, ct)),
        };
        o.Lines = r.Lines.Select(l => new SalesOrderLine { SalesOrderId = o.Id, ProductId = l.ProductId, QtyPacks = l.QtyPacks }).ToList();
        db.SalesOrders.Add(o);
        audit.Record("sales_order.created", nameof(SalesOrder), o.Id, $"{o.OrderNo} for {retailer.Name}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetOrderAsync(o.Id, ct);
    }

    public async Task<List<SalesOrderDto>> OrdersAsync(SalesOrderStatus? status, CancellationToken ct = default)
    {
        var q = db.SalesOrders.AsNoTracking().Where(o => o.BranchId == ctx.BranchId);
        if (IsSalesman) q = q.Where(o => o.TakenByUserId == ctx.UserId || db.Retailers.Any(r => r.Id == o.RetailerId && r.SalesmanUserId == ctx.UserId));
        if (status is not null) q = q.Where(o => o.Status == status);
        var ids = await q.OrderByDescending(o => o.CreatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        var list = new List<SalesOrderDto>();
        foreach (var id in ids) list.Add(await GetOrderAsync(id, ct));
        return list;
    }

    public async Task<SalesOrderDto> GetOrderAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.SalesOrders.AsNoTracking().Include(x => x.Retailer).Include(x => x.Lines).ThenInclude(l => l.Product)
                    .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Order");
        var avail = await AvailablePacksAsync(o.Lines.Select(l => l.ProductId).ToList(), ct);
        var by = o.TakenByUserId is null ? null : await db.Users.Where(u => u.Id == o.TakenByUserId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
        return new SalesOrderDto(o.Id, o.OrderNo, o.RetailerId, o.Retailer!.Name, o.Status, o.Notes, by, o.CreatedAt, o.TradeInvoiceId,
            o.Lines.Select(l => new SalesOrderLineDto(l.ProductId, l.Product!.Name, l.QtyPacks, avail.GetValueOrDefault(l.ProductId))).ToList());
    }

    public async Task CancelOrderAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.SalesOrders.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Order");
        Require(o.Status == SalesOrderStatus.Open, "Only open orders can be cancelled.");
        if (IsSalesman) Require(o.TakenByUserId == ctx.UserId, "You can cancel only your own orders.");
        o.Status = SalesOrderStatus.Cancelled;
        audit.Record("sales_order.cancelled", nameof(SalesOrder), o.Id, o.OrderNo);
        await db.SaveChangesAsync(ct);
    }

    private async Task<Dictionary<Guid, decimal>> AvailablePacksAsync(List<Guid> productIds, CancellationToken ct)
    {
        var today = Today;
        var rows = await db.StockBalances.AsNoTracking()
            .Where(s => s.BranchId == ctx.BranchId && productIds.Contains(s.ProductId) && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .Select(s => new { s.ProductId, s.QtyUnits, s.Batch!.Product!.PackSize }).ToListAsync(ct);
        return rows.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Sum(r => Math.Floor(r.QtyUnits / r.PackSize)));
    }

    // ===================== trade invoice =====================

    public async Task<TradeInvoiceDto> CreateInvoiceAsync(TradeInvoiceRequest r, CancellationToken ct = default)
    {
        Require(r.ClientRequestId != Guid.Empty, "ClientRequestId is required.");
        var existing = await db.TradeInvoices.Where(i => i.ClientRequestId == r.ClientRequestId).Select(i => (Guid?)i.Id).SingleOrDefaultAsync(ct);
        if (existing is not null) return await GetInvoiceAsync(existing.Value, ct);
        Require(r.Lines is { Count: > 0 and <= 300 }, "An invoice needs 1-300 lines.");
        Require(r.Lines.All(l => l.QtyPacks > 0 && l.QtyPacks == decimal.Truncate(l.QtyPacks) && l.DiscountPercent is >= 0 and < 100), "Quantities must be whole packs; discount 0-99%.");
        var retailer = await db.Retailers.SingleOrDefaultAsync(x => x.Id == r.RetailerId && x.IsActive, ct) ?? throw BusinessException.NotFound("Retailer");
        var today = Today;
        if (retailer.DrugLicenseValidTo is { } lv && lv < today)
            throw BusinessException.Conflict("licence_expired", $"{retailer.Name}'s drug licence expired on {lv:dd-MMM-yyyy}. Update the licence record before selling.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var rates = await db.TradeRates.Where(t => productIds.Contains(t.ProductId) && (t.RetailerId == null || t.RetailerId == retailer.Id)).ToListAsync(ct);
        var schemes = await db.TradeSchemes.Where(s => productIds.Contains(s.ProductId) && s.IsActive && s.ValidFrom <= today && s.ValidTo >= today).ToListAsync(ct);

        var inv = new TradeInvoice
        {
            BranchId = branch.Id, RetailerId = retailer.Id, InvoiceDate = today, DueDate = today.AddDays(retailer.CreditDays), InterState = retailer.StateCode != branch.StateCode,
            SalesOrderId = r.SalesOrderId, ClientRequestId = r.ClientRequestId, CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
        };
        var allocations = new List<(TradeInvoiceLine Line, Batch Batch, decimal Units)>();
        var n = 0;
        foreach (var l in r.Lines)
        {
            if (!products.TryGetValue(l.ProductId, out var p) || !p.IsActive) throw BusinessException.Validation("Unknown or inactive product.");
            var rate = rates.FirstOrDefault(t => t.ProductId == p.Id && t.RetailerId == retailer.Id)?.RatePerPack
                       ?? rates.FirstOrDefault(t => t.ProductId == p.Id && t.RetailerId == null)?.RatePerPack
                       ?? throw BusinessException.Validation($"No trade rate set for {p.Name}.");
            var scheme = schemes.Where(s => s.ProductId == p.Id).OrderByDescending(s => s.ValidFrom).FirstOrDefault();
            var free = scheme is { BuyQtyPacks: > 0 } ? Math.Floor(l.QtyPacks / scheme.BuyQtyPacks) * scheme.FreeQtyPacks : 0;
            var discount = Math.Max(l.DiscountPercent, scheme?.DiscountPercent ?? 0);

            // FEFO in whole packs: paid packs first, then free packs, across sellable batches
            var batches = await db.StockBalances.Where(s => s.BranchId == branch.Id && s.ProductId == p.Id && s.QtyUnits >= p.PackSize
                                                           && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
                .OrderBy(s => s.Batch!.ExpiryDate).Select(s => new { s.Batch, Packs = Math.Floor(s.QtyUnits / p.PackSize) }).ToListAsync(ct);
            if (batches.Sum(b => b.Packs) < l.QtyPacks + free)
                throw BusinessException.Conflict("insufficient_stock", $"Only {batches.Sum(b => b.Packs)} packs of {p.Name} available (need {l.QtyPacks + free} incl. {free} free).");
            decimal paidLeft = l.QtyPacks, freeLeft = free;
            foreach (var b in batches)
            {
                if (paidLeft + freeLeft <= 0) break;
                var room = b.Packs;
                var paid = Math.Min(paidLeft, room); room -= paid; paidLeft -= paid;
                var fr = Math.Min(freeLeft, room); freeLeft -= fr;
                if (paid + fr == 0) continue;
                var gross = Money.R2(paid * rate);
                var disc = Money.R2(gross * discount / 100m);
                var taxable = gross - disc;
                var tax = Money.TaxOnExclusive(taxable, p.GstRate);
                var line = new TradeInvoiceLine
                {
                    TradeInvoiceId = inv.Id, LineNo = ++n, ProductId = p.Id, BatchId = b.Batch!.Id, QtyPacks = paid, FreeQtyPacks = fr, RatePerPack = rate,
                    DiscountPercent = discount, GrossAmount = gross, DiscountAmount = disc, TaxableAmount = taxable, GstRate = p.GstRate, TaxAmount = tax,
                    LineTotal = taxable + tax, CostPerUnit = b.Batch.CostPerUnit, SchemeId = scheme?.Id,
                };
                inv.Lines.Add(line);
                allocations.Add((line, b.Batch, (paid + fr) * p.PackSize));
            }
        }
        inv.GrossAmount = inv.Lines.Sum(x => x.GrossAmount);
        inv.DiscountAmount = inv.Lines.Sum(x => x.DiscountAmount);
        inv.TaxableAmount = inv.Lines.Sum(x => x.TaxableAmount);
        inv.TaxAmount = inv.Lines.Sum(x => x.TaxAmount);
        (inv.NetAmount, inv.RoundOff) = Money.RoundToRupee(inv.TaxableAmount + inv.TaxAmount);

        // credit control
        var dto = (await ToDtos([retailer], ct)).Single();
        var problems = new List<string>();
        if (retailer.CreditLimit > 0 && dto.Outstanding + inv.NetAmount > retailer.CreditLimit)
            problems.Add($"credit limit {retailer.CreditLimit:0.00} exceeded (due {dto.Outstanding:0.00} + this bill {inv.NetAmount:0.00})");
        if (dto.Overdue > 0) problems.Add($"{dto.Overdue:0.00} is overdue");
        if (problems.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(r.CreditOverrideReason)) throw BusinessException.Conflict("credit_blocked", $"Blocked: {string.Join("; ", problems)}.");
            if (ctx.Role != UserRole.Owner) throw BusinessException.Forbidden("Only the owner can override a credit block.");
            audit.Record("trade_invoice.credit_override", nameof(Retailer), retailer.Id, $"{string.Join("; ", problems)} — {r.CreditOverrideReason}");
        }

        inv.InvoiceNo = SequenceService.Format(branch.Code, "B", today, await sequences.NextAsync(branch.Id, "B", today, ct));
        db.TradeInvoices.Add(inv);
        foreach (var (line, batch, units) in allocations)
            await inventory.ApplyAsync(branch.Id, batch, -units, MovementType.TradeSale, nameof(TradeInvoice), inv.Id, line.Id, inv.InvoiceNo, ct);
        if (r.SalesOrderId is { } oid)
        {
            var order = await db.SalesOrders.SingleOrDefaultAsync(o => o.Id == oid && o.RetailerId == retailer.Id, ct) ?? throw BusinessException.NotFound("Order");
            Require(order.Status == SalesOrderStatus.Open, "This order is already invoiced or cancelled.");
            order.Status = SalesOrderStatus.Invoiced;
            order.TradeInvoiceId = inv.Id;
        }
        audit.Record("trade_invoice.posted", nameof(TradeInvoice), inv.Id, $"{inv.InvoiceNo} {retailer.Name} {inv.NetAmount}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetInvoiceAsync(inv.Id, ct);
    }

    /// <summary>Convenience: invoice exactly what an open order asked for.</summary>
    public async Task<TradeInvoiceDto> InvoiceOrderAsync(Guid orderId, string? overrideReason, CancellationToken ct = default)
    {
        var o = await db.SalesOrders.AsNoTracking().Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == orderId && x.BranchId == ctx.BranchId, ct)
                ?? throw BusinessException.NotFound("Order");
        return await CreateInvoiceAsync(new TradeInvoiceRequest(o.Id, o.RetailerId, o.Lines.Select(l => new TradeLineInput(l.ProductId, l.QtyPacks)).ToList(), o.Id, overrideReason), ct);
    }

    public async Task<TradeInvoiceDto> CancelInvoiceAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inv = await db.TradeInvoices.Include(i => i.Lines).ThenInclude(l => l.Batch).Include(i => i.Lines).ThenInclude(l => l.Product)
                      .SingleOrDefaultAsync(i => i.Id == id && i.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Invoice");
        Require(inv.Status == TradeDocStatus.Posted, "Already cancelled.");
        if (await db.TradeReceiptAllocations.AnyAsync(a => a.TradeInvoiceId == id && db.TradeReceipts.Any(t => t.Id == a.TradeReceiptId && t.Status == TradeDocStatus.Posted), ct)
            || await db.TradeCreditNotes.AnyAsync(c => c.TradeInvoiceId == id, ct))
            throw BusinessException.Conflict("has_settlements", "Collections or credit notes exist for this invoice; cancel those first or issue a credit note.");
        foreach (var l in inv.Lines)
            await inventory.ApplyAsync(inv.BranchId, l.Batch!, (l.QtyPacks + l.FreeQtyPacks) * l.Product!.PackSize, MovementType.TradeSaleCancel, nameof(TradeInvoice), inv.Id, l.Id, reason, ct);
        inv.Status = TradeDocStatus.Cancelled; inv.CancelReason = reason.Trim(); inv.CancelledAt = clock.UtcNow;
        if (inv.SalesOrderId is { } oid) { var o = await db.SalesOrders.SingleAsync(x => x.Id == oid, ct); o.Status = SalesOrderStatus.Open; o.TradeInvoiceId = null; }
        audit.Record("trade_invoice.cancelled", nameof(TradeInvoice), inv.Id, reason);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetInvoiceAsync(id, ct);
    }

    public async Task<TradeInvoiceDto> GetInvoiceAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.TradeInvoices.AsNoTracking().Include(x => x.Retailer).Include(x => x.Lines).ThenInclude(l => l.Product).Include(x => x.Lines).ThenInclude(l => l.Batch)
                    .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Invoice");
        if (IsSalesman && i.Retailer!.SalesmanUserId != ctx.UserId) throw BusinessException.NotFound("Invoice");
        var outstanding = (await OpenInvoicesAsync([i.RetailerId], ct)).SingleOrDefault(o => o.Doc.Id == id)?.Doc.Outstanding ?? 0;
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == i.BranchId, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        return new TradeInvoiceDto(i.Id, i.InvoiceNo, i.InvoiceDate, i.DueDate, i.Status, i.RetailerId, i.Retailer!.Name, i.Retailer.Gstin, i.Retailer.DrugLicenseNo, i.InterState,
            i.GrossAmount, i.DiscountAmount, i.TaxableAmount, i.TaxAmount, i.RoundOff, i.NetAmount, i.Status == TradeDocStatus.Posted ? outstanding : 0, i.CancelReason,
            i.Lines.OrderBy(l => l.LineNo).Select(l => new TradeLineDto(l.Id, l.LineNo, l.ProductId, l.Product!.Name, l.Product.HsnCode, l.Batch!.BatchNo, l.Batch.ExpiryDate,
                l.QtyPacks, l.FreeQtyPacks, l.RatePerPack, l.DiscountPercent, l.TaxableAmount, l.GstRate, l.TaxAmount, l.LineTotal, l.ReturnedPacks)).ToList(),
            new PrintHeader(tenant.Name, branch.Name, branch.Address, branch.Phone, branch.Gstin, branch.DrugLicenseNo, branch.StateCode));
    }

    public async Task<List<TradeInvoiceListItem>> InvoicesAsync(Guid? retailerId, CancellationToken ct = default)
    {
        var q = db.TradeInvoices.AsNoTracking().Where(i => i.BranchId == ctx.BranchId);
        if (retailerId is not null) q = q.Where(i => i.RetailerId == retailerId);
        if (IsSalesman) q = q.Where(i => i.Retailer!.SalesmanUserId == ctx.UserId);
        var rows = await q.OrderByDescending(i => i.CreatedAt).Take(300)
            .Select(i => new { i.Id, i.InvoiceNo, i.InvoiceDate, i.DueDate, i.Retailer!.Name, i.Status, i.NetAmount, i.RetailerId }).ToListAsync(ct);
        var open = (await OpenInvoicesAsync(rows.Select(r => r.RetailerId).Distinct().ToList(), ct)).ToDictionary(o => o.Doc.Id, o => o.Doc.Outstanding);
        return rows.Select(r => new TradeInvoiceListItem(r.Id, r.InvoiceNo, r.InvoiceDate, r.DueDate, r.Name, r.Status, r.NetAmount, open.GetValueOrDefault(r.Id))).ToList();
    }

    // ===================== credit notes (retailer returns) =====================

    public async Task<CreditNoteDto> CreateCreditNoteAsync(CreditNoteRequest r, CancellationToken ct = default)
    {
        Require(r.ClientRequestId != Guid.Empty, "ClientRequestId is required.");
        var existing = await db.TradeCreditNotes.Where(c => c.ClientRequestId == r.ClientRequestId).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);
        if (existing is not null) return await GetCreditNoteAsync(existing.Value, ct);
        Require(Enum.IsDefined(r.Reason), "Invalid reason.");
        Require(r.Lines is { Count: > 0 }, "Select returned items.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inv = await db.TradeInvoices.Include(i => i.Lines).ThenInclude(l => l.Batch).Include(i => i.Lines).ThenInclude(l => l.Product)
                      .SingleOrDefaultAsync(i => i.Id == r.TradeInvoiceId && i.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Invoice");
        Require(inv.Status == TradeDocStatus.Posted, "Invoice is cancelled.");
        var branch = await db.Branches.SingleAsync(b => b.Id == inv.BranchId, ct);
        var cn = new TradeCreditNote
        {
            BranchId = inv.BranchId, RetailerId = inv.RetailerId, TradeInvoiceId = inv.Id, Date = Today, Reason = r.Reason, Notes = r.Notes?.Trim(),
            ClientRequestId = r.ClientRequestId, CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            CreditNoteNo = SequenceService.Format(branch.Code, "N", Today, await sequences.NextAsync(branch.Id, "N", Today, ct)),
        };
        foreach (var rl in r.Lines.GroupBy(x => x.TradeInvoiceLineId).Select(g => new CreditNoteLineInput(g.Key, g.Sum(x => x.QtyPacks))))
        {
            var line = inv.Lines.SingleOrDefault(l => l.Id == rl.TradeInvoiceLineId) ?? throw BusinessException.Validation("Line not on this invoice.");
            var left = line.QtyPacks - line.ReturnedPacks;
            Require(rl.QtyPacks > 0 && rl.QtyPacks == decimal.Truncate(rl.QtyPacks) && rl.QtyPacks <= left, $"Only {left} billed packs of {line.Product!.Name} can be credited.");
            var taxable = rl.QtyPacks == left
                ? line.TaxableAmount - Money.R2(line.TaxableAmount * line.ReturnedPacks / line.QtyPacks)
                : Money.R2(line.TaxableAmount * rl.QtyPacks / line.QtyPacks);
            var tax = Money.TaxOnExclusive(taxable, line.GstRate);
            line.ReturnedPacks += rl.QtyPacks;
            cn.Lines.Add(new TradeCreditNoteLine { TradeCreditNoteId = cn.Id, TradeInvoiceLineId = line.Id, ProductId = line.ProductId, BatchId = line.BatchId,
                QtyPacks = rl.QtyPacks, TaxableAmount = taxable, TaxAmount = tax });
            var units = rl.QtyPacks * line.Product!.PackSize;
            cn.CostValue += Money.R2(units * line.CostPerUnit);
            if (r.Reason == TradeReturnReason.Saleable)
                await inventory.ApplyAsync(inv.BranchId, line.Batch!, units, MovementType.TradeReturnIn, nameof(TradeCreditNote), cn.Id, line.Id, cn.CreditNoteNo, ct);
        }
        cn.TaxableAmount = cn.Lines.Sum(x => x.TaxableAmount);
        cn.TaxAmount = cn.Lines.Sum(x => x.TaxAmount);
        cn.Amount = cn.TaxableAmount + cn.TaxAmount;
        db.TradeCreditNotes.Add(cn);
        audit.Record("trade_credit_note.posted", nameof(TradeCreditNote), cn.Id, $"{cn.CreditNoteNo} {r.Reason} {cn.Amount}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetCreditNoteAsync(cn.Id, ct);
    }

    private async Task<CreditNoteDto> GetCreditNoteAsync(Guid id, CancellationToken ct) =>
        await db.TradeCreditNotes.AsNoTracking().Where(c => c.Id == id)
            .Select(c => new CreditNoteDto(c.Id, c.CreditNoteNo, c.Date, db.TradeInvoices.Where(i => i.Id == c.TradeInvoiceId).Select(i => i.InvoiceNo).First(),
                c.Reason, c.TaxableAmount, c.TaxAmount, c.Amount)).SingleAsync(ct);

    // ===================== collections & outstanding =====================

    private sealed record OpenRow(Guid RetailerId, OpenDocument Doc);

    /// <summary>Per posted invoice: outstanding = net − credit notes − receipt allocations.</summary>
    private async Task<List<OpenRow>> OpenInvoicesAsync(List<Guid> retailerIds, CancellationToken ct)
    {
        var rows = await db.TradeInvoices.AsNoTracking().Where(i => i.Status == TradeDocStatus.Posted && retailerIds.Contains(i.RetailerId))
            .Select(i => new
            {
                i.Id, i.RetailerId, i.InvoiceNo, i.InvoiceDate, i.DueDate, i.NetAmount, i.CreatedAt,
                Credits = db.TradeCreditNotes.Where(c => c.TradeInvoiceId == i.Id).Sum(c => (decimal?)c.Amount) ?? 0,
                Paid = db.TradeReceiptAllocations.Where(a => a.TradeInvoiceId == i.Id && db.TradeReceipts.Any(t => t.Id == a.TradeReceiptId && t.Status == TradeDocStatus.Posted))
                    .Sum(a => (decimal?)a.Amount) ?? 0,
            }).ToListAsync(ct);
        var today = Today;
        return rows.Select(r => new OpenRow(r.RetailerId, new OpenDocument(r.Id, r.InvoiceNo, r.InvoiceDate, r.DueDate, Math.Max(0, today.DayNumber - r.DueDate.DayNumber),
            r.NetAmount - r.Credits, r.Paid, r.NetAmount - r.Credits - r.Paid, r.CreatedAt))).ToList();
    }

    private async Task<Dictionary<Guid, decimal>> UnallocatedAsync(List<Guid> retailerIds, CancellationToken ct) =>
        (await db.TradeReceipts.AsNoTracking().Where(t => t.Status == TradeDocStatus.Posted && retailerIds.Contains(t.RetailerId))
            .Select(t => new { t.RetailerId, Left = t.Amount - (t.Allocations.Sum(a => (decimal?)a.Amount) ?? 0) }).ToListAsync(ct))
        .GroupBy(x => x.RetailerId).ToDictionary(g => g.Key, g => g.Sum(x => x.Left));

    public async Task<TradeReceiptDto> CreateReceiptAsync(TradeReceiptRequest r, CancellationToken ct = default)
    {
        Require(r.Amount > 0, "Amount must be positive.");
        Require(Enum.IsDefined(r.Mode), "Invalid mode.");
        Require(r.Mode == SettlementMode.Cash || !string.IsNullOrWhiteSpace(r.Reference), "Reference (cheque/UTR) is required for non-cash collections.");
        var retailer = await Visible().SingleOrDefaultAsync(x => x.Id == r.RetailerId, ct) ?? throw BusinessException.NotFound("Retailer");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var open = (await OpenInvoicesAsync([retailer.Id], ct)).Select(o => o.Doc).Where(d => d.Outstanding > 0).ToDictionary(d => d.Id);
        var amount = Money.R2(r.Amount);
        var alloc = new List<AllocationInput>();
        if (r.Allocations is { Count: > 0 })
        {
            foreach (var a in r.Allocations)
            {
                if (!open.TryGetValue(a.DocumentId, out var d)) throw BusinessException.Validation("Invoice not open for this retailer.");
                Require(a.Amount > 0 && a.Amount <= d.Outstanding, $"Allocation to {d.DocumentNo} exceeds its outstanding {d.Outstanding:0.00}.");
                alloc.Add(a);
            }
            Require(alloc.Sum(a => a.Amount) <= amount, "Allocations exceed the amount.");
        }
        else
        {
            var left = amount;
            foreach (var d in open.Values.OrderBy(d => d.DueDate).ThenBy(d => d.EnteredAt))
            {
                if (left <= 0) break;
                var take = Math.Min(left, d.Outstanding);
                alloc.Add(new AllocationInput(d.Id, take));
                left -= take;
            }
        }
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var rc = new TradeReceipt
        {
            BranchId = branch.Id, RetailerId = retailer.Id, Date = Today, Mode = r.Mode, Amount = amount, Reference = r.Reference?.Trim(),
            CollectedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            ReceiptNo = SequenceService.Format(branch.Code, "Y", Today, await sequences.NextAsync(branch.Id, "Y", Today, ct)),
        };
        rc.Allocations = alloc.Select(a => new TradeReceiptAllocation { TradeReceiptId = rc.Id, TradeInvoiceId = a.DocumentId, Amount = a.Amount }).ToList();
        db.TradeReceipts.Add(rc);
        audit.Record("trade_receipt.posted", nameof(TradeReceipt), rc.Id, $"{rc.ReceiptNo} {retailer.Name} {amount} {r.Mode}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ReceiptsAsync(retailer.Id, ct)).Single(x => x.Id == rc.Id);
    }

    public async Task<TradeReceiptDto> CancelReceiptAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required (e.g. cheque bounced).");
        var rc = await db.TradeReceipts.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Receipt");
        Require(rc.Status == TradeDocStatus.Posted, "Already cancelled.");
        rc.Status = TradeDocStatus.Cancelled; rc.CancelReason = reason.Trim();
        audit.Record("trade_receipt.cancelled", nameof(TradeReceipt), rc.Id, reason);
        await db.SaveChangesAsync(ct);
        return (await ReceiptsAsync(rc.RetailerId, ct)).Single(x => x.Id == id);
    }

    public async Task<List<TradeReceiptDto>> ReceiptsAsync(Guid? retailerId, CancellationToken ct = default)
    {
        var q = db.TradeReceipts.AsNoTracking().Where(t => t.BranchId == ctx.BranchId);
        if (retailerId is not null) q = q.Where(t => t.RetailerId == retailerId);
        if (IsSalesman) q = q.Where(t => t.CollectedByUserId == ctx.UserId);
        return await q.OrderByDescending(t => t.CreatedAt).Take(300)
            .Select(t => new TradeReceiptDto(t.Id, t.ReceiptNo, t.Date, t.RetailerId, t.Retailer!.Name, t.Mode, t.Amount, t.Allocations.Sum(a => a.Amount), t.Reference, t.Status,
                db.Users.Where(u => u.Id == t.CollectedByUserId).Select(u => u.DisplayName).FirstOrDefault()))
            .ToListAsync(ct);
    }

    public async Task<RetailerStatement> StatementAsync(Guid retailerId, CancellationToken ct = default)
    {
        var r = await Visible().AsNoTracking().SingleOrDefaultAsync(x => x.Id == retailerId, ct) ?? throw BusinessException.NotFound("Retailer");
        var dto = (await ToDtos([r], ct)).Single();
        var inv = await db.TradeInvoices.AsNoTracking().Where(i => i.RetailerId == retailerId && i.Status == TradeDocStatus.Posted)
            .Select(i => new { i.InvoiceDate, i.InvoiceNo, i.NetAmount }).ToListAsync(ct);
        var cns = await db.TradeCreditNotes.AsNoTracking().Where(c => c.RetailerId == retailerId).Select(c => new { c.Date, c.CreditNoteNo, c.Amount }).ToListAsync(ct);
        var rcs = await db.TradeReceipts.AsNoTracking().Where(t => t.RetailerId == retailerId && t.Status == TradeDocStatus.Posted)
            .Select(t => new { t.Date, t.ReceiptNo, t.Amount, t.Mode }).ToListAsync(ct);
        var entries = inv.Select(i => (i.InvoiceDate, "Invoice", i.InvoiceNo, i.NetAmount, 0m, (string?)null))
            .Concat(cns.Select(c => (c.Date, "Credit note", c.CreditNoteNo, 0m, c.Amount, (string?)null)))
            .Concat(rcs.Select(t => (t.Date, "Collection", t.ReceiptNo, 0m, t.Amount, (string?)t.Mode.ToString()))).OrderBy(e => e.Item1).ToList();
        var bal = 0m;
        var lines = entries.Select(e => { bal += e.Item4 - e.Item5; return new StatementLine(e.Item1, e.Item2, e.Item3, e.Item4, e.Item5, bal, e.Item6); }).ToList();
        var open = (await OpenInvoicesAsync([retailerId], ct)).Select(o => o.Doc).Where(d => d.Outstanding > 0).OrderBy(d => d.DueDate).ToList();
        return new RetailerStatement(r.Id, r.Name, dto.Outstanding, dto.Overdue, r.CreditLimit, r.CreditLimit > 0 ? r.CreditLimit - dto.Outstanding : 0, open, lines);
    }

    public async Task<TradeAging> AgingAsync(CancellationToken ct = default)
    {
        var retailers = await Visible().AsNoTracking().ToListAsync(ct);
        var docs = (await OpenInvoicesAsync(retailers.Select(r => r.Id).ToList(), ct)).Select(o => o.Doc).Where(d => d.Outstanding > 0).ToList();
        AgingBucket B(string l, Func<OpenDocument, bool> f) { var x = docs.Where(f).ToList(); return new AgingBucket(l, x.Sum(d => d.Outstanding), x.Count); }
        var dtos = await ToDtos(retailers, ct);
        return new TradeAging([B("Not due", d => d.DaysOverdue == 0), B("1-30 days", d => d.DaysOverdue is >= 1 and <= 30), B("31-60 days", d => d.DaysOverdue is >= 31 and <= 60),
            B("61-90 days", d => d.DaysOverdue is >= 61 and <= 90), B("90+ days", d => d.DaysOverdue > 90)], dtos.Sum(d => d.Outstanding),
            dtos.Where(d => d.Overdue > 0).OrderByDescending(d => d.Overdue).Take(20).ToList());
    }

    /// <summary>Sales = posted trade invoices (net) by the retailer's assigned salesman; collections = posted receipts collected by the user.</summary>
    public async Task<List<SalesmanRow>> SalesmanReportAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var inv = await db.TradeInvoices.AsNoTracking().Where(i => i.BranchId == ctx.BranchId && i.Status == TradeDocStatus.Posted && i.InvoiceDate >= from && i.InvoiceDate <= to)
            .Select(i => new { i.Retailer!.SalesmanUserId, i.NetAmount }).ToListAsync(ct);
        var rc = await db.TradeReceipts.AsNoTracking().Where(t => t.BranchId == ctx.BranchId && t.Status == TradeDocStatus.Posted && t.Date >= from && t.Date <= to)
            .Select(t => new { t.CollectedByUserId, t.Amount }).ToListAsync(ct);
        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5));
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(5.5));
        var orders = await db.SalesOrders.AsNoTracking().Where(o => o.BranchId == ctx.BranchId && o.CreatedAt >= fromUtc && o.CreatedAt <= toUtc)
            .Select(o => o.TakenByUserId).ToListAsync(ct);
        var ids = inv.Select(i => i.SalesmanUserId).Concat(rc.Select(r => r.CollectedByUserId)).Concat(orders).Distinct().ToList();
        var names = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return ids.Select(id => new SalesmanRow(id, id is { } g && names.TryGetValue(g, out var n) ? n : "Unassigned / office",
            inv.Count(i => i.SalesmanUserId == id), inv.Where(i => i.SalesmanUserId == id).Sum(i => i.NetAmount),
            rc.Where(r => r.CollectedByUserId == id).Sum(r => r.Amount), orders.Count(o => o == id))).OrderByDescending(x => x.Sales).ToList();
    }

    [GeneratedRegex("^[0-9]{2}$")] private static partial Regex StateRx();
    [GeneratedRegex("^[0-9]{2}[A-Z0-9]{10}[0-9A-Z]{3}$")] private static partial Regex GstinRx();
}
