using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record BranchDto(Guid Id, string Name, string Code, string? Address, string? Phone, string? Gstin, string StateCode,
    string? DrugLicenseNo, DateOnly? DrugLicenseValidTo, bool IsWarehouse, bool IsActive, bool OnlineOrdersEnabled = false, string? DeliveryPincodes = null, string? OnlineNotice = null,
    decimal DeliveryCharge = 0, decimal? FreeDeliveryAbove = null, decimal DeliveryChargeGstRate = 18m);
public record BranchInput(string Name, string Code, string? Address, string? Phone, string? Gstin, string StateCode,
    string? DrugLicenseNo, DateOnly? DrugLicenseValidTo, bool IsWarehouse, bool IsActive);

public record TransferLineInput(Guid BatchId, decimal QtyUnits);
public record TransferInput(Guid ToBranchId, string? Notes, List<TransferLineInput> Lines, Guid? IndentId = null);
public record ReceiveLineInput(Guid LineId, decimal ReceivedQtyUnits);
public record ReceiveRequest(List<ReceiveLineInput> Lines, string? Notes);
public record TransferLineDto(Guid Id, Guid ProductId, string ProductName, string BaseUnit, Guid BatchId, string BatchNo, DateOnly ExpiryDate,
    decimal QtyUnits, decimal? ReceivedQtyUnits, decimal? ShortageUnits, decimal CostPerUnit);
public record TransferDto(Guid Id, string TransferNo, Guid FromBranchId, string FromBranch, Guid ToBranchId, string ToBranch, TransferStatus Status,
    string? Notes, string? ReceiveNotes, DateTimeOffset CreatedAt, DateTimeOffset? DispatchedAt, DateTimeOffset? ReceivedAt, decimal Value,
    decimal ShortageValue, string Direction, List<TransferLineDto> Lines);
public record TransferListItem(Guid Id, string TransferNo, string FromBranch, string ToBranch, TransferStatus Status, DateTimeOffset CreatedAt,
    DateTimeOffset? DispatchedAt, int LineCount, string Direction);

public record IndentLineInput(Guid ProductId, decimal QtyUnits);
public record IndentInput(Guid SupplyingBranchId, string? Notes, List<IndentLineInput> Lines);
public record IndentDto(Guid Id, string IndentNo, Guid RequestingBranchId, string RequestingBranch, Guid SupplyingBranchId, string SupplyingBranch,
    IndentStatus Status, string? Notes, string? RejectReason, DateTimeOffset CreatedAt, string Direction, Guid? TransferId,
    List<IndentLineDto> Lines);
public record IndentLineDto(Guid ProductId, string ProductName, string BaseUnit, decimal QtyUnits, decimal AvailableAtSupplier);

public record BranchComparisonRow(Guid BranchId, string Branch, bool IsWarehouse, int Bills, decimal NetSales, decimal Returns, decimal GrossMargin,
    decimal MarginPercent, decimal StockValue, decimal NearExpiryValue, decimal CustomerDues, decimal InTransitOut);
public record BranchComparison(DateOnly From, DateOnly To, List<BranchComparisonRow> Rows, BranchComparisonRow Total);

/// <summary>Branches, inter-branch transfers, indents and cross-branch reporting.</summary>
public partial class MultiStoreService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory,
    SequenceService sequences, AuditService audit, SubscriptionService subscription)
{
    private bool SeesAllBranches => ctx.Role is UserRole.Owner or UserRole.Accountant;

    // ---------- branches ----------

    public async Task<List<BranchDto>> BranchesAsync(CancellationToken ct = default)
    {
        var q = db.Branches.AsNoTracking();
        if (!SeesAllBranches)
        {
            var home = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DefaultBranchId).SingleAsync(ct);
            q = q.Where(b => b.Id == home || b.Id == ctx.BranchId);
        }
        return await q.OrderBy(b => b.IsWarehouse).ThenBy(b => b.Name).Select(b => ToDto(b)).ToListAsync(ct);
    }

    /// <summary>Branches any user may send transfers/indents to (names only are exposed).</summary>
    public async Task<List<BranchDto>> AllActiveBranchesAsync(CancellationToken ct = default) =>
        await db.Branches.AsNoTracking().Where(b => b.IsActive).OrderBy(b => b.Name).Select(b => ToDto(b)).ToListAsync(ct);

    public async Task<BranchDto> SaveBranchAsync(Guid? id, BranchInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name), "Branch name is required.");
        Require(System.Text.RegularExpressions.Regex.IsMatch(i.Code ?? "", "^[A-Za-z0-9]{1,4}$"), "Branch code must be 1-4 letters/digits.");
        Require(System.Text.RegularExpressions.Regex.IsMatch(i.StateCode ?? "", "^[0-9]{2}$"), "State code must be 2 digits.");
        var code = i.Code!.ToUpperInvariant();
        if (await db.Branches.AnyAsync(b => b.Code == code && b.Id != id, ct)) throw BusinessException.Conflict("duplicate_code", "Another branch uses this code.");
        Branch b;
        if (id is null) { await subscription.EnsureCanAddBranchAsync(ct); b = new Branch(); db.Branches.Add(b); }
        else
        {
            b = await db.Branches.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Branch");
            if (b.Code != code && await db.SalesInvoices.AnyAsync(s => s.BranchId == b.Id, ct))
                throw BusinessException.Validation("Branch code is part of issued document numbers and can't change once billing has started.");
            if (!i.IsActive && await db.StockBalances.AnyAsync(s => s.BranchId == b.Id && s.QtyUnits > 0, ct))
                throw BusinessException.Validation("Move or adjust this branch's stock before deactivating it.");
        }
        b.Name = i.Name.Trim(); b.Code = code; b.Address = i.Address?.Trim(); b.Phone = i.Phone?.Trim();
        b.Gstin = string.IsNullOrWhiteSpace(i.Gstin) ? null : i.Gstin.Trim().ToUpperInvariant(); b.StateCode = i.StateCode!;
        b.DrugLicenseNo = i.DrugLicenseNo?.Trim(); b.DrugLicenseValidTo = i.DrugLicenseValidTo; b.IsWarehouse = i.IsWarehouse; b.IsActive = i.IsActive;
        audit.Record(id is null ? "branch.created" : "branch.updated", nameof(Branch), b.Id, $"{b.Code} {b.Name}");
        await db.SaveChangesAsync(ct);
        return ToDto(b);
    }

    public async Task AssignUserBranchAsync(Guid userId, Guid branchId, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw BusinessException.NotFound("User");
        if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct)) throw BusinessException.NotFound("Branch");
        u.DefaultBranchId = branchId;
        audit.Record("user.branch_assigned", nameof(AppUser), u.Id, $"{u.Email} → {branchId}");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Validates that the current user may work in the given branch.</summary>
    public async Task EnsureCanUseBranchAsync(Guid branchId, CancellationToken ct = default)
    {
        if (!await db.Branches.AnyAsync(b => b.Id == branchId && b.IsActive, ct)) throw BusinessException.NotFound("Branch");
        if (SeesAllBranches) return;
        var home = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DefaultBranchId).SingleAsync(ct);
        if (home != branchId) throw BusinessException.Forbidden("You can only work in your assigned branch.");
    }

    private static BranchDto ToDto(Branch b) => new(b.Id, b.Name, b.Code, b.Address, b.Phone, b.Gstin, b.StateCode, b.DrugLicenseNo,
        b.DrugLicenseValidTo, b.IsWarehouse, b.IsActive, b.OnlineOrdersEnabled, b.DeliveryPincodes, b.OnlineNotice, b.DeliveryCharge, b.FreeDeliveryAbove, b.DeliveryChargeGstRate);

    // ---------- transfers ----------

    public async Task<TransferDto> CreateTransferAsync(TransferInput r, CancellationToken ct = default)
    {
        Require(r.ToBranchId != ctx.BranchId, "Choose a different destination branch.");
        Require(r.Lines is { Count: > 0 and <= 500 }, "A transfer needs 1-500 lines.");
        if (!await db.Branches.AnyAsync(b => b.Id == r.ToBranchId && b.IsActive, ct)) throw BusinessException.NotFound("Destination branch");
        var batchIds = r.Lines.Select(l => l.BatchId).Distinct().ToList();
        Require(batchIds.Count == r.Lines.Count, "Each batch can appear once.");
        var batches = await db.Batches.Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var onHand = await db.StockBalances.Where(s => s.BranchId == ctx.BranchId && batchIds.Contains(s.BatchId)).ToDictionaryAsync(s => s.BatchId, s => s.QtyUnits, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var t = new StockTransfer
        {
            FromBranchId = branch.Id, ToBranchId = r.ToBranchId, Notes = r.Notes?.Trim(), IndentId = r.IndentId, CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
            TransferNo = SequenceService.Format(branch.Code, "T", today, await sequences.NextAsync(branch.Id, "T", today, ct)),
        };
        var n = 0;
        foreach (var l in r.Lines)
        {
            n++;
            if (!batches.TryGetValue(l.BatchId, out var b)) throw BusinessException.Validation($"Line {n}: batch not found.");
            Require(l.QtyUnits > 0 && l.QtyUnits == decimal.Truncate(l.QtyUnits), $"Line {n}: quantity must be a positive whole number.");
            if (l.QtyUnits > onHand.GetValueOrDefault(l.BatchId)) throw BusinessException.Validation($"Line {n}: only {onHand.GetValueOrDefault(l.BatchId)} units of batch {b.BatchNo} here.");
            t.Lines.Add(new StockTransferLine { StockTransferId = t.Id, ProductId = b.ProductId, BatchId = b.Id, QtyUnits = l.QtyUnits, CostPerUnit = b.CostPerUnit });
        }
        db.StockTransfers.Add(t);
        audit.Record("transfer.created", nameof(StockTransfer), t.Id, $"{t.TransferNo} → {r.ToBranchId}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetTransferAsync(t.Id, ct);
    }

    public async Task<TransferDto> DispatchAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await LoadTransfer(id, ct);
        if (t.FromBranchId != ctx.BranchId) throw BusinessException.Forbidden("Only the sending branch can dispatch.");
        if (t.Status != TransferStatus.Draft) throw BusinessException.Conflict("not_draft", "Already dispatched or cancelled.");
        foreach (var l in t.Lines)
            await inventory.ApplyAsync(t.FromBranchId, l.Batch!, -l.QtyUnits, MovementType.TransferOut, nameof(StockTransfer), t.Id, l.Id, t.TransferNo, ct);
        t.Status = TransferStatus.Dispatched;
        t.DispatchedAt = clock.UtcNow;
        t.DispatchedByUserId = ctx.UserId;
        audit.Record("transfer.dispatched", nameof(StockTransfer), t.Id, t.TransferNo);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetTransferAsync(id, ct);
    }

    /// <summary>Destination confirms what physically arrived. Missing units are recorded as a shortage (transit loss).</summary>
    public async Task<TransferDto> ReceiveAsync(Guid id, ReceiveRequest r, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var t = await LoadTransfer(id, ct);
        if (t.ToBranchId != ctx.BranchId) throw BusinessException.Forbidden("Only the receiving branch can receive this transfer.");
        if (t.Status != TransferStatus.Dispatched) throw BusinessException.Conflict("not_in_transit", "This transfer is not in transit.");
        Require(r.Lines.Count == t.Lines.Count && r.Lines.Select(x => x.LineId).ToHashSet().SetEquals(t.Lines.Select(x => x.Id)),
            "Enter the received quantity for every line.");
        foreach (var rl in r.Lines)
        {
            var l = t.Lines.Single(x => x.Id == rl.LineId);
            Require(rl.ReceivedQtyUnits >= 0 && rl.ReceivedQtyUnits == decimal.Truncate(rl.ReceivedQtyUnits), "Received quantity must be a whole number ≥ 0.");
            if (rl.ReceivedQtyUnits > l.QtyUnits) throw BusinessException.Validation($"Received more than dispatched for batch {l.Batch!.BatchNo}. Count again or record the extra as a stock adjustment.");
            l.ReceivedQtyUnits = rl.ReceivedQtyUnits;
            if (rl.ReceivedQtyUnits > 0)
                await inventory.ApplyAsync(t.ToBranchId, l.Batch!, rl.ReceivedQtyUnits, MovementType.TransferIn, nameof(StockTransfer), t.Id, l.Id, t.TransferNo, ct);
        }
        var shortage = t.Lines.Any(l => l.ReceivedQtyUnits < l.QtyUnits);
        if (shortage) Require(!string.IsNullOrWhiteSpace(r.Notes), "Explain the shortage (e.g. broken in transit, short packed).");
        t.Status = TransferStatus.Received;
        t.ReceivedAt = clock.UtcNow;
        t.ReceivedByUserId = ctx.UserId;
        t.ReceiveNotes = r.Notes?.Trim();
        audit.Record("transfer.received", nameof(StockTransfer), t.Id, $"{t.TransferNo}{(shortage ? " with shortage: " + r.Notes : "")}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetTransferAsync(id, ct);
    }

    public async Task CancelTransferAsync(Guid id, CancellationToken ct = default)
    {
        var t = await LoadTransfer(id, ct);
        if (t.FromBranchId != ctx.BranchId) throw BusinessException.Forbidden("Only the sending branch can cancel.");
        if (t.Status != TransferStatus.Draft) throw BusinessException.Conflict("not_draft", "Only draft transfers can be cancelled; dispatched stock must be received.");
        t.Status = TransferStatus.Cancelled;
        audit.Record("transfer.cancelled", nameof(StockTransfer), t.Id, t.TransferNo);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<TransferListItem>> ListTransfersAsync(TransferStatus? status, CancellationToken ct = default)
    {
        var b = ctx.BranchId;
        var q = db.StockTransfers.AsNoTracking().Where(t => t.FromBranchId == b || t.ToBranchId == b);
        if (status is not null) q = q.Where(t => t.Status == status);
        var names = await db.Branches.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var rows = await q.OrderByDescending(t => t.CreatedAt).Take(200)
            .Select(t => new { t.Id, t.TransferNo, t.FromBranchId, t.ToBranchId, t.Status, t.CreatedAt, t.DispatchedAt, Count = t.Lines.Count }).ToListAsync(ct);
        return rows.Select(t => new TransferListItem(t.Id, t.TransferNo, names[t.FromBranchId], names[t.ToBranchId], t.Status, t.CreatedAt, t.DispatchedAt, t.Count,
            t.FromBranchId == b ? "out" : "in")).ToList();
    }

    public async Task<TransferDto> GetTransferAsync(Guid id, CancellationToken ct = default)
    {
        var t = await db.StockTransfers.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Product).Include(x => x.Lines).ThenInclude(l => l.Batch)
                    .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Transfer");
        if (t.FromBranchId != ctx.BranchId && t.ToBranchId != ctx.BranchId && !SeesAllBranches) throw BusinessException.NotFound("Transfer");
        var names = await db.Branches.AsNoTracking().Where(b => b.Id == t.FromBranchId || b.Id == t.ToBranchId).ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var lines = t.Lines.OrderBy(l => l.Product!.Name).Select(l => new TransferLineDto(l.Id, l.ProductId, l.Product!.Name, l.Product.BaseUnit, l.BatchId,
            l.Batch!.BatchNo, l.Batch.ExpiryDate, l.QtyUnits, l.ReceivedQtyUnits, l.ReceivedQtyUnits is null ? null : l.QtyUnits - l.ReceivedQtyUnits, l.CostPerUnit)).ToList();
        return new TransferDto(t.Id, t.TransferNo, t.FromBranchId, names[t.FromBranchId], t.ToBranchId, names[t.ToBranchId], t.Status, t.Notes, t.ReceiveNotes,
            t.CreatedAt, t.DispatchedAt, t.ReceivedAt, Money.R2(lines.Sum(l => l.QtyUnits * l.CostPerUnit)),
            Money.R2(lines.Sum(l => (l.ShortageUnits ?? 0) * l.CostPerUnit)), t.FromBranchId == ctx.BranchId ? "out" : "in", lines);
    }

    private async Task<StockTransfer> LoadTransfer(Guid id, CancellationToken ct) =>
        await db.StockTransfers.Include(t => t.Lines).ThenInclude(l => l.Batch).SingleOrDefaultAsync(t => t.Id == id, ct)
        ?? throw BusinessException.NotFound("Transfer");

    // ---------- indents ----------

    public async Task<IndentDto> CreateIndentAsync(IndentInput r, CancellationToken ct = default)
    {
        Require(r.SupplyingBranchId != ctx.BranchId, "Choose the branch/warehouse that should supply.");
        Require(r.Lines is { Count: > 0 and <= 500 }, "An indent needs 1-500 lines.");
        if (!await db.Branches.AnyAsync(b => b.Id == r.SupplyingBranchId && b.IsActive, ct)) throw BusinessException.NotFound("Supplying branch");
        var ids = r.Lines.Select(l => l.ProductId).Distinct().ToList();
        Require(ids.Count == r.Lines.Count, "Each product can appear once.");
        if (await db.Products.CountAsync(p => ids.Contains(p.Id), ct) != ids.Count) throw BusinessException.Validation("Unknown product on indent.");
        Require(r.Lines.All(l => l.QtyUnits > 0 && l.QtyUnits == decimal.Truncate(l.QtyUnits)), "Quantities must be positive whole numbers.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var i = new StockIndent
        {
            RequestingBranchId = branch.Id, SupplyingBranchId = r.SupplyingBranchId, Notes = r.Notes?.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
            IndentNo = SequenceService.Format(branch.Code, "I", today, await sequences.NextAsync(branch.Id, "I", today, ct)),
        };
        i.Lines = r.Lines.Select(l => new StockIndentLine { StockIndentId = i.Id, ProductId = l.ProductId, QtyUnits = l.QtyUnits }).ToList();
        db.StockIndents.Add(i);
        audit.Record("indent.created", nameof(StockIndent), i.Id, i.IndentNo);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetIndentAsync(i.Id, ct);
    }

    /// <summary>Supplying branch turns the indent into a draft transfer, picking batches First-Expiry-First-Out (sellable stock only).</summary>
    public async Task<TransferDto> FulfilIndentAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.StockIndents.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Indent");
        if (i.SupplyingBranchId != ctx.BranchId) throw BusinessException.Forbidden("Only the supplying branch can fulfil this indent.");
        if (i.Status != IndentStatus.Requested) throw BusinessException.Conflict("not_open", "This indent is already handled.");
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var lines = new List<TransferLineInput>();
        foreach (var l in i.Lines)
        {
            var left = l.QtyUnits;
            var stock = await db.StockBalances.Where(s => s.BranchId == ctx.BranchId && s.ProductId == l.ProductId && s.QtyUnits > 0
                                                          && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
                .OrderBy(s => s.Batch!.ExpiryDate).Select(s => new { s.BatchId, s.QtyUnits }).ToListAsync(ct);
            foreach (var s in stock)
            {
                if (left <= 0) break;
                var take = Math.Min(left, s.QtyUnits);
                lines.Add(new TransferLineInput(s.BatchId, take));
                left -= take;
            }
        }
        if (lines.Count == 0) throw BusinessException.Conflict("no_stock", "None of the requested items are in stock here.");
        var transfer = await CreateTransferAsync(new TransferInput(i.RequestingBranchId, $"Against indent {i.IndentNo}", lines, i.Id), ct);
        i.Status = IndentStatus.Fulfilled;
        audit.Record("indent.fulfilled", nameof(StockIndent), i.Id, $"{i.IndentNo} → {transfer.TransferNo}");
        await db.SaveChangesAsync(ct);
        return transfer;
    }

    public async Task<IndentDto> RejectIndentAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        var i = await db.StockIndents.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Indent");
        if (i.SupplyingBranchId != ctx.BranchId) throw BusinessException.Forbidden("Only the supplying branch can reject this indent.");
        if (i.Status != IndentStatus.Requested) throw BusinessException.Conflict("not_open", "This indent is already handled.");
        i.Status = IndentStatus.Rejected;
        i.RejectReason = reason.Trim();
        audit.Record("indent.rejected", nameof(StockIndent), i.Id, $"{i.IndentNo}: {reason}");
        await db.SaveChangesAsync(ct);
        return await GetIndentAsync(id, ct);
    }

    public async Task<List<IndentDto>> ListIndentsAsync(CancellationToken ct = default)
    {
        var ids = await db.StockIndents.AsNoTracking().Where(i => i.RequestingBranchId == ctx.BranchId || i.SupplyingBranchId == ctx.BranchId)
            .OrderByDescending(i => i.CreatedAt).Take(100).Select(i => i.Id).ToListAsync(ct);
        var list = new List<IndentDto>();
        foreach (var id in ids) list.Add(await GetIndentAsync(id, ct));
        return list;
    }

    public async Task<IndentDto> GetIndentAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.StockIndents.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Product).SingleOrDefaultAsync(x => x.Id == id, ct)
                ?? throw BusinessException.NotFound("Indent");
        if (i.RequestingBranchId != ctx.BranchId && i.SupplyingBranchId != ctx.BranchId && !SeesAllBranches) throw BusinessException.NotFound("Indent");
        var names = await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct);
        var productIds = i.Lines.Select(l => l.ProductId).ToList();
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var avail = await db.StockBalances.AsNoTracking()
            .Where(s => s.BranchId == i.SupplyingBranchId && productIds.Contains(s.ProductId) && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .GroupBy(s => s.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var transferId = await db.StockTransfers.Where(t => t.IndentId == id).Select(t => (Guid?)t.Id).FirstOrDefaultAsync(ct);
        return new IndentDto(i.Id, i.IndentNo, i.RequestingBranchId, names[i.RequestingBranchId], i.SupplyingBranchId, names[i.SupplyingBranchId], i.Status,
            i.Notes, i.RejectReason, i.CreatedAt, i.RequestingBranchId == ctx.BranchId ? "raised" : "received", transferId,
            i.Lines.Select(l => new IndentLineDto(l.ProductId, l.Product!.Name, l.Product.BaseUnit, l.QtyUnits, avail.GetValueOrDefault(l.ProductId))).ToList());
    }

    // ---------- consolidated reporting ----------

    /// <summary>
    /// Per-branch comparison for a date range (business dates). Gross margin = (sales taxable − returns taxable) − (COGS − returned cost).
    /// Stock and near-expiry (≤ 90 days, incl. expired) at current batch cost. Customer dues = credit sold − credit-note refunds − receipts.
    /// </summary>
    public async Task<BranchComparison> CompareAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        if (!SeesAllBranches) throw BusinessException.Forbidden("Only the owner or accountant can compare branches.");
        Require(from <= to, "From must be before To.");
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var nearUntil = today.AddDays(90);
        var rows = new List<BranchComparisonRow>();
        foreach (var b in await db.Branches.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync(ct))
        {
            var inv = db.SalesInvoices.AsNoTracking().Where(s => s.BranchId == b.Id && s.Status == SalesStatus.Posted && s.BusinessDate >= from && s.BusinessDate <= to);
            var bills = await inv.CountAsync(ct);
            var net = await inv.SumAsync(s => (decimal?)s.NetAmount, ct) ?? 0;
            var taxable = await inv.SumAsync(s => (decimal?)s.TaxableAmount, ct) ?? 0;
            var cogs = await db.SalesInvoiceLines.Where(l => inv.Select(s => s.Id).Contains(l.SalesInvoiceId)).SumAsync(l => (decimal?)(l.QtyUnits * l.CostPerUnit), ct) ?? 0;
            var ret = await db.SalesReturnLines.AsNoTracking()
                .Where(l => db.SalesReturns.Any(r => r.Id == l.SalesReturnId && r.BranchId == b.Id && r.BusinessDate >= from && r.BusinessDate <= to))
                .Join(db.SalesInvoiceLines, l => l.SalesInvoiceLineId, il => il.Id, (l, il) => new { l.Amount, l.QtyUnits, il.GstRate, il.CostPerUnit }).ToListAsync(ct);
            var retTaxable = ret.Sum(x => Money.SplitInclusive(x.Amount, x.GstRate).Taxable);
            var retCost = ret.Sum(x => x.QtyUnits * x.CostPerUnit);
            var margin = Money.R2(taxable - retTaxable - (cogs - retCost));
            var netTaxable = taxable - retTaxable;
            var stock = await db.StockBalances.Where(s => s.BranchId == b.Id && s.QtyUnits > 0).SumAsync(s => (decimal?)(s.QtyUnits * s.Batch!.CostPerUnit), ct) ?? 0;
            var near = await db.StockBalances.Where(s => s.BranchId == b.Id && s.QtyUnits > 0 && s.Batch!.ExpiryDate <= nearUntil)
                .SumAsync(s => (decimal?)(s.QtyUnits * s.Batch!.CostPerUnit), ct) ?? 0;
            var creditSold = await db.SalesPayments.Where(p => p.Mode == PaymentMode.Credit && db.SalesInvoices.Any(s => s.Id == p.SalesInvoiceId && s.BranchId == b.Id && s.Status == SalesStatus.Posted))
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
            var creditRefunds = await db.SalesReturns.Where(r => r.BranchId == b.Id && r.RefundMode == PaymentMode.Credit).SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0;
            var receipts = await db.CustomerReceipts.Where(r => r.BranchId == b.Id && r.Status == DocStatus.Posted).SumAsync(r => (decimal?)r.Amount, ct) ?? 0;
            var transit = await db.StockTransferLines.Where(l => db.StockTransfers.Any(t => t.Id == l.StockTransferId && t.FromBranchId == b.Id && t.Status == TransferStatus.Dispatched))
                .SumAsync(l => (decimal?)(l.QtyUnits * l.CostPerUnit), ct) ?? 0;
            rows.Add(new BranchComparisonRow(b.Id, b.Name, b.IsWarehouse, bills, net, ret.Sum(x => x.Amount), margin,
                netTaxable == 0 ? 0 : Money.R2(margin * 100 / netTaxable), Money.R2(stock), Money.R2(near), creditSold - creditRefunds - receipts, Money.R2(transit)));
        }
        var totalTaxableProxy = rows.Sum(r => r.MarginPercent == 0 ? 0 : r.GrossMargin * 100 / r.MarginPercent);
        var total = new BranchComparisonRow(Guid.Empty, "All branches", false, rows.Sum(r => r.Bills), rows.Sum(r => r.NetSales), rows.Sum(r => r.Returns),
            rows.Sum(r => r.GrossMargin), totalTaxableProxy == 0 ? 0 : Money.R2(rows.Sum(r => r.GrossMargin) * 100 / totalTaxableProxy),
            rows.Sum(r => r.StockValue), rows.Sum(r => r.NearExpiryValue), rows.Sum(r => r.CustomerDues), rows.Sum(r => r.InTransitOut));
        return new BranchComparison(from, to, rows, total);
    }
}
