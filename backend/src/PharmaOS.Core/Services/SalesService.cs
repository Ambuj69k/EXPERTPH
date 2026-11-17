using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record SaleLineInput(Guid ProductId, Guid? BatchId, decimal QtyUnits, decimal DiscountPercent);
public record PaymentInput(PaymentMode Mode, decimal Amount, string? Reference);
public record SaleRequest(Guid ClientRequestId, CustomerInput? Customer, string? DoctorName,
    List<SaleLineInput> Lines, List<PaymentInput> Payments, DateTimeOffset? CapturedAt = null, string? OfflineRef = null,
    decimal DeliveryCharge = 0);

public record SaleLineDto(Guid Id, int LineNo, Guid ProductId, string ProductName, string? HsnCode, DrugSchedule Schedule,
    decimal PackSize, string PackUnit, string BaseUnit, Guid BatchId, string BatchNo, DateOnly ExpiryDate, decimal QtyUnits,
    decimal MrpPerUnit, decimal GrossAmount, decimal DiscountPercent, decimal DiscountAmount, decimal GstRate,
    decimal TaxableAmount, decimal TaxAmount, decimal NetAmount, decimal ReturnedQtyUnits);
public record PaymentDto(PaymentMode Mode, decimal Amount, string? Reference);
public record SaleDto(Guid Id, string InvoiceNo, DateTimeOffset InvoiceAt, DateOnly BusinessDate, SalesStatus Status,
    Guid? CustomerId, string? CustomerName, string? CustomerPhone, string? DoctorName, decimal GrossAmount,
    decimal DiscountAmount, decimal TaxableAmount, decimal TaxAmount, decimal RoundOff, decimal NetAmount,
    string? CancelReason, List<SaleLineDto> Lines, List<PaymentDto> Payments, PrintHeader Header, bool CapturedOffline = false, string? OfflineRef = null,
    decimal DeliveryCharge = 0, decimal DeliveryChargeTaxable = 0, decimal DeliveryChargeTax = 0, decimal DeliveryChargeGstRate = 0);
public record PrintHeader(string BusinessName, string BranchName, string? Address, string? Phone, string? Gstin,
    string? DrugLicenseNo, string StateCode);
public record SaleListItem(Guid Id, string InvoiceNo, DateTimeOffset InvoiceAt, SalesStatus Status, string? CustomerName,
    string? CustomerPhone, decimal NetAmount, int LineCount);

public record ReturnLineInput(Guid SalesInvoiceLineId, decimal QtyUnits);
public record SalesReturnRequest(Guid ClientRequestId, Guid SalesInvoiceId, string Reason, PaymentMode RefundMode,
    List<ReturnLineInput> Lines);
public record SalesReturnDto(Guid Id, string ReturnNo, Guid SalesInvoiceId, string InvoiceNo, DateTimeOffset ReturnAt,
    string Reason, PaymentMode RefundMode, decimal RefundAmount, List<SalesReturnLineDto> Lines);
public record SalesReturnLineDto(Guid SalesInvoiceLineId, string ProductName, string BatchNo, decimal QtyUnits, decimal Amount);

public class SalesService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    MasterDataService masters, SequenceService sequences, AuditService audit, LedgerService ledger, LoyaltyService loyalty, CrmService crm)
{
    private static readonly DrugSchedule[] PrescriberRequired = [DrugSchedule.H1, DrugSchedule.X, DrugSchedule.NarcoticPsychotropic];

    public async Task<SaleDto> CreateAsync(SaleRequest r, CancellationToken ct = default)
    {
        Require(r.ClientRequestId != Guid.Empty, "ClientRequestId is required.");
        var existing = await db.SalesInvoices.AsNoTracking().Where(s => s.ClientRequestId == r.ClientRequestId)
            .Select(s => (Guid?)s.Id).SingleOrDefaultAsync(ct);
        if (existing is not null) return await GetAsync(existing.Value, ct); // idempotent retry

        Require(r.Lines is { Count: > 0 and <= 200 }, "A bill needs 1-200 lines.");
        Require(r.Payments is { Count: > 0 and <= 5 }, "At least one payment is required.");
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        if (tenant.RequireOpenShift && !await db.CashShifts.AnyAsync(s => s.BranchId == ctx.BranchId && s.UserId == ctx.UserId && s.Status == ShiftStatus.Open, ct))
            throw BusinessException.Conflict("shift_required", "Open your cash shift before billing.");
        foreach (var l in r.Lines)
        {
            Require(l.QtyUnits > 0, "Quantity must be greater than zero.");
            Require(l.DiscountPercent is >= 0 and <= 100, "Discount must be 0-100%.");
            if (ctx.Role == UserRole.Cashier && l.DiscountPercent > tenant.CashierMaxDiscountPercent)
                throw BusinessException.Forbidden($"Cashier discount limit is {tenant.CashierMaxDiscountPercent}%. Ask a pharmacist/owner.");
        }

        // Offline-captured bills keep the time they were actually made (within a bounded window).
        var now = clock.UtcNow;
        if (r.CapturedAt is { } captured)
        {
            Require(captured <= now.AddMinutes(5), "Capture time is in the future — check the device clock.");
            Require(captured >= now.AddHours(-72), "Offline bills must be synced within 72 hours. Enter it as a fresh bill instead.");
            Require(r.OfflineRef is { Length: > 0 and <= 30 }, "Offline reference is required for offline bills.");
            now = captured;
        }
        var businessDate = BusinessCalendar.LocalDate(now, tenant.TimeZone);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
            if (branch.IsWarehouse) throw BusinessException.Conflict("warehouse_no_billing", "This branch is a warehouse; counter billing is disabled.");
            var productIds = r.Lines.Select(l => l.ProductId).Distinct().ToList();
            var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
            var prices = await BranchPricesAsync(branch.Id, productIds, ct);

            var invoice = new SalesInvoice
            {
                BranchId = branch.Id, InvoiceAt = now, BusinessDate = businessDate, ClientRequestId = r.ClientRequestId,
                CapturedOffline = r.CapturedAt is not null, OfflineRef = r.CapturedAt is null ? null : r.OfflineRef,
                CreatedByUserId = ctx.UserId, DoctorName = string.IsNullOrWhiteSpace(r.DoctorName) ? null : r.DoctorName.Trim(),
            };

            var allocations = new List<(SalesInvoiceLine Line, Batch Batch)>();
            var lineNo = 0;
            foreach (var l in r.Lines)
            {
                if (!products.TryGetValue(l.ProductId, out var product) || !product.IsActive)
                    throw BusinessException.Validation("A product on this bill is missing or inactive.");
                if (!product.AllowLooseSale && l.QtyUnits % product.PackSize != 0)
                    throw BusinessException.Validation($"{product.Name} can only be sold in full {product.PackUnit}s ({product.PackSize} {product.BaseUnit}).");
                if (l.QtyUnits != decimal.Truncate(l.QtyUnits))
                    throw BusinessException.Validation($"{product.Name}: quantity must be a whole number of {product.BaseUnit}.");

                foreach (var (batch, qty) in await AllocateAsync(product, l.BatchId, l.QtyUnits, businessDate, ct))
                {
                    var (mrpPerUnit, gross, disc, discPct, net) = Pricing.Line(qty, batch.Mrp, product.PackSize, prices.GetValueOrDefault(product.Id), l.DiscountPercent);
                    var (taxable, tax) = Money.SplitInclusive(net, product.GstRate);
                    var line = new SalesInvoiceLine
                    {
                        SalesInvoiceId = invoice.Id, LineNo = ++lineNo, ProductId = product.Id, BatchId = batch.Id,
                        QtyUnits = qty, MrpPerUnit = mrpPerUnit, GrossAmount = gross, DiscountPercent = discPct,
                        DiscountAmount = disc, GstRate = product.GstRate, TaxableAmount = taxable, TaxAmount = tax,
                        NetAmount = net, CostPerUnit = batch.CostPerUnit,
                    };
                    invoice.Lines.Add(line);
                    allocations.Add((line, batch));
                }
            }

            invoice.GrossAmount = invoice.Lines.Sum(x => x.GrossAmount);
            invoice.DiscountAmount = invoice.Lines.Sum(x => x.DiscountAmount);
            // optional home-delivery charge (GST inclusive), taxed at the branch's configured rate
            var delivery = Money.R2(r.DeliveryCharge);
            Require(delivery is >= 0 and <= 5000, "Delivery charge must be between 0 and 5000.");
            if (delivery > 0)
            {
                (invoice.DeliveryChargeTaxable, invoice.DeliveryChargeTax) = Money.SplitInclusive(delivery, branch.DeliveryChargeGstRate);
                invoice.DeliveryCharge = delivery;
                invoice.DeliveryChargeGstRate = branch.DeliveryChargeGstRate;
            }
            invoice.TaxableAmount = invoice.Lines.Sum(x => x.TaxableAmount) + invoice.DeliveryChargeTaxable;
            invoice.TaxAmount = invoice.Lines.Sum(x => x.TaxAmount) + invoice.DeliveryChargeTax;
            (invoice.NetAmount, invoice.RoundOff) = Money.RoundToRupee(invoice.Lines.Sum(x => x.NetAmount) + delivery);

            // Customer: optional for cash, mandatory for credit and for prescriber-required schedules.
            var needsPrescriber = invoice.Lines.Any(x => PrescriberRequired.Contains(products[x.ProductId].Schedule));
            var hasCredit = r.Payments.Any(p => p.Mode == PaymentMode.Credit);
            if (r.Customer is not null && (!string.IsNullOrWhiteSpace(r.Customer.Name) || !string.IsNullOrWhiteSpace(r.Customer.Phone)))
            {
                var customer = await masters.FindOrCreateCustomerAsync(r.Customer, ct);
                invoice.CustomerId = customer.Id;
                invoice.CustomerName = customer.Name;
                invoice.CustomerPhone = customer.Phone;
            }
            if (hasCredit && invoice.CustomerId is null) throw BusinessException.Validation("Credit sale requires a customer with phone number.");
            if (hasCredit)
            {
                // FindAsync also sees a customer created moments ago in this unit of work (not yet saved).
                var customer = await db.Customers.FindAsync([invoice.CustomerId!.Value], ct)
                               ?? throw BusinessException.NotFound("Customer");
                if (customer.CreditLimit is { } limit)
                {
                    var newCredit = r.Payments.Where(p => p.Mode == PaymentMode.Credit).Sum(p => Money.R2(p.Amount));
                    var balance = await ledger.CustomerBalanceAsync(customer.Id, ct);
                    if (balance + newCredit > limit)
                        throw BusinessException.Conflict("credit_limit",
                            $"Credit limit {limit:0.00} exceeded: current due {balance:0.00} + this bill {newCredit:0.00}.");
                }
            }
            if (needsPrescriber && (invoice.CustomerName is null || invoice.DoctorName is null))
                throw BusinessException.Validation("This bill contains a scheduled drug that needs patient and prescriber details.");

            // Payments must match the bill exactly. Card/UPI here means "recorded by staff", not "settled".
            foreach (var p in r.Payments)
            {
                Require(Enum.IsDefined(p.Mode), "Invalid payment mode.");
                Require(p.Amount > 0, "Payment amounts must be positive.");
                invoice.Payments.Add(new SalesPayment
                {
                    SalesInvoiceId = invoice.Id, Mode = p.Mode, Amount = Money.R2(p.Amount),
                    Reference = string.IsNullOrWhiteSpace(p.Reference) ? null : p.Reference.Trim(),
                });
            }
            var paid = invoice.Payments.Sum(p => p.Amount);
            if (paid != invoice.NetAmount)
                throw BusinessException.Validation($"Payments ({paid:0.00}) must equal bill total ({invoice.NetAmount:0.00}).");
            var loyaltyAmount = invoice.Payments.Where(p => p.Mode == PaymentMode.Loyalty).Sum(p => p.Amount);
            var redeemedPoints = loyaltyAmount > 0 ? await loyalty.ValidateRedemptionAsync(invoice.CustomerId, loyaltyAmount, ct) : 0;

            var number = await sequences.NextAsync(branch.Id, "S", businessDate, ct);
            invoice.InvoiceNo = SequenceService.Format(branch.Code, "S", businessDate, number);
            db.SalesInvoices.Add(invoice);

            foreach (var (line, batch) in allocations)
                await inventory.ApplyAsync(branch.Id, batch, -line.QtyUnits, MovementType.Sale, nameof(SalesInvoice),
                    invoice.Id, line.Id, invoice.InvoiceNo, ct);

            await loyalty.RecordSaleAsync(invoice, redeemedPoints, ct);
            if (invoice.CustomerId is { } cid)
                await crm.OnSalePostedAsync(cid, invoice.Lines.Select(l => l.ProductId), businessDate, ct);
            audit.Record("sale.posted", nameof(SalesInvoice), invoice.Id, $"{invoice.InvoiceNo} net {invoice.NetAmount}");
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetAsync(invoice.Id, ct);
        }
        catch (DbUpdateException)
        {
            // A concurrent retry of the same submit may have won the race; if so, return that invoice.
            if (!await IsDuplicateRequestAsync(r.ClientRequestId)) throw;
            var id = await db.SalesInvoices.AsNoTracking().Where(s => s.ClientRequestId == r.ClientRequestId).Select(s => s.Id).SingleAsync(ct);
            return await GetAsync(id, ct);
        }
    }

    /// <summary>Bill total the same lines would produce right now (FEFO batches, MRP, rounding) — nothing is saved.</summary>
    public async Task<decimal> PreviewTotalAsync(List<SaleLineInput> lines, CancellationToken ct = default, decimal deliveryCharge = 0)
    {
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow, tenant.TimeZone);
        var total = 0m;
        var prices = await BranchPricesAsync(ctx.BranchId, lines.Select(l => l.ProductId).Distinct().ToList(), ct);
        foreach (var l in lines)
        {
            var product = await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == l.ProductId, ct) ?? throw BusinessException.NotFound("Product");
            foreach (var (batch, qty) in await AllocateAsync(product, l.BatchId, l.QtyUnits, today, ct))
            {
                total += Pricing.Line(qty, batch.Mrp, product.PackSize, prices.GetValueOrDefault(product.Id), l.DiscountPercent).Net;
            }
        }
        return Money.RoundToRupee(total + Money.R2(deliveryCharge)).Rounded;
    }

    private async Task<Dictionary<Guid, decimal?>> BranchPricesAsync(Guid branchId, List<Guid> productIds, CancellationToken ct) =>
        await db.BranchProductPrices.AsNoTracking().Where(p => p.BranchId == branchId && productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, p => (decimal?)p.PricePerPack, ct);

    private async Task<bool> IsDuplicateRequestAsync(Guid clientRequestId)
    {
        db.ChangeTracker.Clear();
        if (db.Database.CurrentTransaction is { } t) await t.RollbackAsync();
        return await db.SalesInvoices.AsNoTracking().AnyAsync(s => s.ClientRequestId == clientRequestId);
    }

    /// <summary>Explicit batch if chosen at the counter; otherwise First-Expiry-First-Out across sellable batches.</summary>
    private async Task<List<(Batch, decimal)>> AllocateAsync(Product product, Guid? batchId, decimal qty, DateOnly today, CancellationToken ct)
    {
        if (batchId is not null)
        {
            var batch = await db.Batches.SingleOrDefaultAsync(b => b.Id == batchId && b.ProductId == product.Id, ct)
                        ?? throw BusinessException.Validation($"Selected batch does not belong to {product.Name}.");
            if (batch.ExpiryDate < today)
                throw BusinessException.Conflict("batch_expired", $"Batch {batch.BatchNo} of {product.Name} is expired.");
            if (batch.IsBlocked)
                throw BusinessException.Conflict("batch_blocked", $"Batch {batch.BatchNo} of {product.Name} is blocked: {batch.BlockReason}");
            return [(batch, qty)];
        }

        var candidates = await db.StockBalances
            .Where(s => s.BranchId == ctx.BranchId && s.ProductId == product.Id && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .OrderBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.Batch!.CreatedAt)
            .Select(s => new { s.Batch, s.QtyUnits })
            .ToListAsync(ct);

        var result = new List<(Batch, decimal)>();
        var remaining = qty;
        foreach (var c in candidates)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, c.QtyUnits);
            result.Add((c.Batch!, take));
            remaining -= take;
        }
        if (remaining > 0)
            throw BusinessException.Conflict("insufficient_stock",
                $"Only {qty - remaining} {product.BaseUnit} of {product.Name} available in non-expired stock.");
        return result;
    }

    public async Task<SaleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.SalesInvoices.AsNoTracking()
            .Include(x => x.Lines).ThenInclude(l => l.Product)
            .Include(x => x.Lines).ThenInclude(l => l.Batch)
            .Include(x => x.Payments)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct)
            ?? throw BusinessException.NotFound("Invoice");
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == s.BranchId, ct);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        return new SaleDto(s.Id, s.InvoiceNo, s.InvoiceAt, s.BusinessDate, s.Status, s.CustomerId, s.CustomerName,
            s.CustomerPhone, s.DoctorName, s.GrossAmount, s.DiscountAmount, s.TaxableAmount, s.TaxAmount, s.RoundOff,
            s.NetAmount, s.CancelReason,
            s.Lines.OrderBy(l => l.LineNo).Select(l => new SaleLineDto(l.Id, l.LineNo, l.ProductId, l.Product!.Name,
                l.Product.HsnCode, l.Product.Schedule, l.Product.PackSize, l.Product.PackUnit, l.Product.BaseUnit, l.BatchId,
                l.Batch!.BatchNo, l.Batch.ExpiryDate, l.QtyUnits, l.MrpPerUnit, l.GrossAmount, l.DiscountPercent,
                l.DiscountAmount, l.GstRate, l.TaxableAmount, l.TaxAmount, l.NetAmount, l.ReturnedQtyUnits)).ToList(),
            s.Payments.Select(p => new PaymentDto(p.Mode, p.Amount, p.Reference)).ToList(),
            new PrintHeader(tenant.Name, branch.Name, branch.Address, branch.Phone, branch.Gstin, branch.DrugLicenseNo, branch.StateCode),
            s.CapturedOffline, s.OfflineRef, s.DeliveryCharge, s.DeliveryChargeTaxable, s.DeliveryChargeTax, s.DeliveryChargeGstRate);
    }

    public async Task<PagedResult<SaleListItem>> ListAsync(DateOnly? from, DateOnly? to, string? q, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var query = db.SalesInvoices.AsNoTracking().Where(s => s.BranchId == ctx.BranchId);
        if (from is not null) query = query.Where(s => s.BusinessDate >= from);
        if (to is not null) query = query.Where(s => s.BusinessDate <= to);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var t = q.Trim();
            query = query.Where(s => s.InvoiceNo.Contains(t) || s.CustomerPhone == t || (s.CustomerName != null && s.CustomerName.Contains(t)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(s => s.InvoiceAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new SaleListItem(s.Id, s.InvoiceNo, s.InvoiceAt, s.Status, s.CustomerName, s.CustomerPhone, s.NetAmount, s.Lines.Count))
            .ToListAsync(ct);
        return new PagedResult<SaleListItem>(items, total, page, pageSize);
    }

    /// <summary>Full cancellation restores stock. Not allowed once any return exists. Non-owners: same business day only.</summary>
    public async Task<SaleDto> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "A cancellation reason is required.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = await db.SalesInvoices.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct)
                ?? throw BusinessException.NotFound("Invoice");
        if (s.Status != SalesStatus.Posted) throw BusinessException.Conflict("not_posted", "Invoice is already cancelled.");
        if (s.Lines.Any(l => l.ReturnedQtyUnits > 0))
            throw BusinessException.Conflict("has_returns", "Invoice has returns and cannot be cancelled. Use a return for remaining items.");
        if (await db.CustomerReceiptAllocations.AnyAsync(a => a.SalesInvoiceId == s.Id
                && db.CustomerReceipts.Any(r => r.Id == a.CustomerReceiptId && r.Status == DocStatus.Posted), ct))
            throw BusinessException.Conflict("has_receipts", "Customer payments are allocated to this bill. Cancel those receipts first.");
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        if (ctx.Role != UserRole.Owner && s.BusinessDate != BusinessCalendar.LocalDate(clock.UtcNow, tenant.TimeZone))
            throw BusinessException.Forbidden("Only the owner can cancel a bill from a previous day.");

        foreach (var line in s.Lines)
        {
            var batch = await db.Batches.SingleAsync(b => b.Id == line.BatchId, ct);
            await inventory.ApplyAsync(s.BranchId, batch, line.QtyUnits, MovementType.SaleCancel, nameof(SalesInvoice), s.Id, line.Id, reason, ct);
        }
        await loyalty.ReverseSaleAsync(s, ct);
        s.Status = SalesStatus.Cancelled;
        s.CancelledAt = clock.UtcNow;
        s.CancelledByUserId = ctx.UserId;
        s.CancelReason = reason.Trim();
        audit.Record("sale.cancelled", nameof(SalesInvoice), s.Id, $"{s.InvoiceNo}: {reason}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SalesReturnDto> CreateReturnAsync(SalesReturnRequest r, CancellationToken ct = default)
    {
        Require(r.ClientRequestId != Guid.Empty, "ClientRequestId is required.");
        var existing = await db.SalesReturns.AsNoTracking().Where(x => x.ClientRequestId == r.ClientRequestId)
            .Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
        if (existing is not null) return await GetReturnAsync(existing.Value, ct);

        Require(!string.IsNullOrWhiteSpace(r.Reason), "Return reason is required.");
        Require(r.Lines is { Count: > 0 }, "Select at least one item to return.");
        Require(Enum.IsDefined(r.RefundMode) && r.RefundMode != PaymentMode.Loyalty, "Invalid refund mode.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var invoice = await db.SalesInvoices.Include(x => x.Lines)
            .SingleOrDefaultAsync(x => x.Id == r.SalesInvoiceId && x.BranchId == ctx.BranchId, ct)
            ?? throw BusinessException.NotFound("Invoice");
        if (invoice.Status != SalesStatus.Posted) throw BusinessException.Conflict("not_posted", "Cannot return against a cancelled invoice.");
        if (r.RefundMode == PaymentMode.Credit && invoice.CustomerId is null)
            throw BusinessException.Validation("Credit-note refund needs a customer on the original invoice.");

        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        var now = clock.UtcNow;
        var businessDate = BusinessCalendar.LocalDate(now, tenant.TimeZone);
        var ret = new SalesReturn
        {
            BranchId = invoice.BranchId, SalesInvoiceId = invoice.Id, ReturnAt = now, BusinessDate = businessDate,
            Reason = r.Reason.Trim(), RefundMode = r.RefundMode, ClientRequestId = r.ClientRequestId, CreatedByUserId = ctx.UserId,
        };

        foreach (var rl in r.Lines.GroupBy(x => x.SalesInvoiceLineId).Select(g => new ReturnLineInput(g.Key, g.Sum(x => x.QtyUnits))))
        {
            var line = invoice.Lines.SingleOrDefault(l => l.Id == rl.SalesInvoiceLineId)
                       ?? throw BusinessException.Validation("Return line does not belong to this invoice.");
            var remaining = line.QtyUnits - line.ReturnedQtyUnits;
            Require(rl.QtyUnits > 0 && rl.QtyUnits == decimal.Truncate(rl.QtyUnits), "Return quantity must be a positive whole number.");
            if (rl.QtyUnits > remaining)
                throw BusinessException.Validation($"Only {remaining} units of line {line.LineNo} can still be returned.");

            decimal amount;
            if (rl.QtyUnits == remaining)
            {
                // Final return on this line takes the exact remainder so totals never drift by a paisa.
                var alreadyRefunded = await db.SalesReturnLines.Where(x => x.SalesInvoiceLineId == line.Id).SumAsync(x => x.Amount, ct);
                amount = line.NetAmount - alreadyRefunded;
            }
            else amount = Money.R2(line.NetAmount * rl.QtyUnits / line.QtyUnits);

            line.ReturnedQtyUnits += rl.QtyUnits;
            var retLine = new SalesReturnLine
            {
                SalesReturnId = ret.Id, SalesInvoiceLineId = line.Id, ProductId = line.ProductId, BatchId = line.BatchId,
                QtyUnits = rl.QtyUnits, Amount = amount,
            };
            ret.Lines.Add(retLine);
        }
        ret.RefundAmount = ret.Lines.Sum(x => x.Amount);

        var branch = await db.Branches.SingleAsync(b => b.Id == invoice.BranchId, ct);
        var number = await sequences.NextAsync(branch.Id, "R", businessDate, ct);
        ret.ReturnNo = SequenceService.Format(branch.Code, "R", businessDate, number);
        db.SalesReturns.Add(ret);

        foreach (var rl in ret.Lines)
        {
            var batch = await db.Batches.SingleAsync(b => b.Id == rl.BatchId, ct);
            await inventory.ApplyAsync(branch.Id, batch, rl.QtyUnits, MovementType.SaleReturn, nameof(SalesReturn), ret.Id, rl.Id, ret.ReturnNo, ct);
        }
        await loyalty.ReverseForReturnAsync(invoice, ret, ct);
        audit.Record("sale.returned", nameof(SalesReturn), ret.Id, $"{ret.ReturnNo} against {invoice.InvoiceNo} refund {ret.RefundAmount}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetReturnAsync(ret.Id, ct);
    }

    public async Task<SalesReturnDto> GetReturnAsync(Guid id, CancellationToken ct = default)
    {
        var r = await db.SalesReturns.AsNoTracking().Include(x => x.Lines)
                    .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct)
                ?? throw BusinessException.NotFound("Return");
        var invoiceNo = await db.SalesInvoices.Where(s => s.Id == r.SalesInvoiceId).Select(s => s.InvoiceNo).SingleAsync(ct);
        var lineIds = r.Lines.Select(l => l.SalesInvoiceLineId).ToList();
        var names = await db.SalesInvoiceLines.Where(l => lineIds.Contains(l.Id))
            .Select(l => new { l.Id, l.Product!.Name, l.Batch!.BatchNo }).ToDictionaryAsync(x => x.Id, ct);
        return new SalesReturnDto(r.Id, r.ReturnNo, r.SalesInvoiceId, invoiceNo, r.ReturnAt, r.Reason, r.RefundMode, r.RefundAmount,
            r.Lines.Select(l => new SalesReturnLineDto(l.SalesInvoiceLineId, names[l.SalesInvoiceLineId].Name,
                names[l.SalesInvoiceLineId].BatchNo, l.QtyUnits, l.Amount)).ToList());
    }

    public async Task<List<SalesReturnDto>> ListReturnsForInvoiceAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var ids = await db.SalesReturns.Where(r => r.SalesInvoiceId == invoiceId && r.BranchId == ctx.BranchId)
            .OrderBy(r => r.ReturnAt).Select(r => r.Id).ToListAsync(ct);
        var list = new List<SalesReturnDto>();
        foreach (var id in ids) list.Add(await GetReturnAsync(id, ct));
        return list;
    }
}
