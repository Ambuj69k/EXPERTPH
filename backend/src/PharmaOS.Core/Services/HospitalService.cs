using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------
public record DepartmentInput(string Name, string Code, bool IsActive);
public record DepartmentDto(Guid Id, string Name, string Code, bool IsActive, int Staff);
public record StaffDepartmentInput(Guid? DepartmentId);
public record FormularyInput(Guid ProductId, bool InFormulary, bool IsHighAlert, string? Notes);
public record FormularyRow(Guid ProductId, string ProductName, string? Composition, DrugSchedule Schedule, string BaseUnit, bool IsHighAlert, bool NeedsSecondCheck,
    string? Notes, decimal StockUnits);
public record QtyLineInput(Guid ProductId, decimal QtyUnits);
public record WardIndentInput(Guid DepartmentId, IndentPriority Priority, string? EncounterRef, string? Notes, List<QtyLineInput> Lines);
public record WardIndentLineDto(Guid Id, Guid ProductId, string ProductName, string BaseUnit, decimal QtyUnits, decimal IssuedUnits, decimal PendingUnits, decimal StockUnits);
public record WardIndentDto(Guid Id, string IndentNo, Guid DepartmentId, string DepartmentName, WardIndentStatus Status, IndentPriority Priority, string? EncounterRef,
    string? Notes, string? RequestedBy, DateTimeOffset CreatedAt, DateTimeOffset? FirstIssuedAt, string? CancelReason, int Version, List<WardIndentLineDto> Lines);
public record WardIssueLineInput(Guid ProductId, decimal QtyUnits, Guid? WardIndentLineId);
public record WardIssueInput(Guid DepartmentId, Guid? WardIndentId, string? ReceivedBy, List<WardIssueLineInput> Lines);
public record WardIssueBatchDto(Guid Id, string BatchNo, DateOnly ExpiryDate, decimal QtyUnits, decimal ReturnedUnits, decimal CostPerUnit);
public record WardIssueLineDto(Guid Id, Guid ProductId, string ProductName, string BaseUnit, decimal QtyUnits, bool HighAlert, List<WardIssueBatchDto> Batches);
public record WardIssueDto(Guid Id, string IssueNo, Guid DepartmentId, string DepartmentName, string? IndentNo, WardIssueStatus Status, bool NeedsSecondCheck,
    string PreparedBy, DateTimeOffset PreparedAt, string? CheckedBy, DateTimeOffset? IssuedAt, string? ReceivedBy, decimal CostValue, string? CancelReason,
    int Version, List<WardIssueLineDto> Lines);
public record WardReturnLineInput(Guid WardIssueBatchId, decimal QtyUnits);
public record WardReturnInput(Guid WardIssueId, WardReturnReason Reason, string? Notes, List<WardReturnLineInput> Lines);
public record WardReturnDto(Guid Id, string ReturnNo, string IssueNo, string DepartmentName, WardReturnReason Reason, decimal CostValue, DateTimeOffset CreatedAt);
public record DepartmentConsumption(Guid DepartmentId, string DepartmentName, int Issues, decimal IssuedValue, decimal ReturnedValue, decimal NetValue,
    int OpenIndents, decimal? AvgHoursToFirstIssue);
public record ProductConsumption(Guid ProductId, string ProductName, string BaseUnit, decimal Units, decimal Value);
public record ConsumptionReport(List<DepartmentConsumption> Departments, List<ProductConsumption> TopProducts, decimal TotalNet);

/// <summary>
/// Hospital pharmacy store → wards. Wards indent formulary items; the pharmacy issues them (FEFO, whole base units) and the cost is
/// charged to the department. High-alert items and narcotic/Schedule X drugs need a second pharmacist to verify before stock moves.
/// Ward staff see only their own department. Patient-level dispensing and billing stay in the HIS.
/// </summary>
public class HospitalService(PharmaDbContext db, ITenantContext ctx, IClock clock, InventoryService inventory, SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);
    private bool IsWard => ctx.Role == UserRole.WardStaff;
    private Guid? _myDept;

    private async Task<Guid> MyDepartmentAsync(CancellationToken ct)
    {
        _myDept ??= await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DepartmentId).SingleAsync(ct)
                    ?? throw BusinessException.Forbidden("You are not assigned to a department yet. Ask the pharmacy to assign you.");
        return _myDept.Value;
    }

    private async Task EnsureDepartmentAccessAsync(Guid departmentId, CancellationToken ct)
    {
        if (IsWard && await MyDepartmentAsync(ct) != departmentId) throw BusinessException.NotFound("Department");
    }

    private static bool ControlledSchedule(DrugSchedule s) => s is DrugSchedule.X or DrugSchedule.NarcoticPsychotropic;

    // ===================== departments & staff =====================

    public async Task<List<DepartmentDto>> DepartmentsAsync(CancellationToken ct = default)
    {
        var q = db.Departments.AsNoTracking();
        if (IsWard) { var mine = await MyDepartmentAsync(ct); q = q.Where(d => d.Id == mine); }
        return await q.OrderBy(d => d.Name).Select(d => new DepartmentDto(d.Id, d.Name, d.Code, d.IsActive, db.Users.Count(u => u.DepartmentId == d.Id && u.IsActive)))
            .ToListAsync(ct);
    }

    public async Task<DepartmentDto> SaveDepartmentAsync(Guid? id, DepartmentInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name) && !string.IsNullOrWhiteSpace(i.Code) && i.Code.Trim().Length <= 10, "Name and a code of up to 10 characters are required.");
        var code = i.Code.Trim().ToUpperInvariant();
        if (await db.Departments.AnyAsync(d => d.Code == code && d.Id != id, ct)) throw BusinessException.Conflict("duplicate", "Another department has this code.");
        Department d;
        if (id is null) { d = new Department(); db.Departments.Add(d); }
        else d = await db.Departments.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Department");
        d.Name = i.Name.Trim(); d.Code = code; d.IsActive = i.IsActive;
        audit.Record(id is null ? "department.created" : "department.updated", nameof(Department), d.Id, $"{code} {d.Name}");
        await db.SaveChangesAsync(ct);
        return (await DepartmentsAsync(ct)).Single(x => x.Id == d.Id);
    }

    public async Task SetStaffDepartmentAsync(Guid userId, Guid? departmentId, CancellationToken ct = default)
    {
        var u = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct) ?? throw BusinessException.NotFound("User");
        Require(u.Role == UserRole.WardStaff || departmentId is null, "Only ward staff are assigned to a department.");
        if (departmentId is { } d && !await db.Departments.AnyAsync(x => x.Id == d && x.IsActive, ct)) throw BusinessException.NotFound("Department");
        u.DepartmentId = departmentId;
        audit.Record("user.department", nameof(AppUser), u.Id, departmentId?.ToString() ?? "none");
        await db.SaveChangesAsync(ct);
    }

    // ===================== formulary =====================

    public async Task<List<FormularyRow>> FormularyAsync(string? q, CancellationToken ct = default)
    {
        var query = db.FormularyItems.AsNoTracking().Where(f => f.Product!.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); query = query.Where(f => f.Product!.Name.Contains(s) || f.Product.Composition!.Contains(s)); }
        var today = Today;
        return (await query.OrderBy(f => f.Product!.Name).Take(500).Select(f => new
        {
            f.ProductId, f.Product!.Name, f.Product.Composition, f.Product.Schedule, f.Product.BaseUnit, f.IsHighAlert, f.Notes,
            Stock = db.StockBalances.Where(b => b.BranchId == ctx.BranchId && b.ProductId == f.ProductId && b.Batch!.ExpiryDate >= today && !b.Batch.IsBlocked).Sum(b => (decimal?)b.QtyUnits) ?? 0,
        }).ToListAsync(ct)).Select(f => new FormularyRow(f.ProductId, f.Name, f.Composition, f.Schedule, f.BaseUnit, f.IsHighAlert, f.IsHighAlert || ControlledSchedule(f.Schedule),
            f.Notes, f.Stock)).ToList();
    }

    public async Task SetFormularyAsync(FormularyInput i, CancellationToken ct = default)
    {
        var product = await db.Products.SingleOrDefaultAsync(p => p.Id == i.ProductId, ct) ?? throw BusinessException.NotFound("Product");
        var f = await db.FormularyItems.SingleOrDefaultAsync(x => x.ProductId == i.ProductId, ct);
        if (!i.InFormulary) { if (f is not null) db.FormularyItems.Remove(f); }
        else
        {
            f ??= db.FormularyItems.Add(new FormularyItem { ProductId = i.ProductId }).Entity;
            f.IsHighAlert = i.IsHighAlert; f.Notes = string.IsNullOrWhiteSpace(i.Notes) ? null : i.Notes.Trim();
        }
        audit.Record("formulary.set", nameof(Product), product.Id, i.InFormulary ? $"in formulary{(i.IsHighAlert ? ", HIGH-ALERT" : "")}" : "removed");
        await db.SaveChangesAsync(ct);
    }

    // ===================== indents =====================

    public async Task<WardIndentDto> CreateIndentAsync(WardIndentInput i, CancellationToken ct = default)
    {
        await EnsureDepartmentAccessAsync(i.DepartmentId, ct);
        var dept = await db.Departments.SingleOrDefaultAsync(d => d.Id == i.DepartmentId && d.IsActive, ct) ?? throw BusinessException.NotFound("Department");
        Require(i.Lines.Count is > 0 and <= 100, "Add 1–100 items.");
        Require(i.Lines.All(l => l.QtyUnits > 0 && l.QtyUnits == Math.Floor(l.QtyUnits)), "Quantities must be whole units (tablets, vials, ...).");
        Require(i.Lines.Select(l => l.ProductId).Distinct().Count() == i.Lines.Count, "List each item once.");
        var ids = i.Lines.Select(l => l.ProductId).ToList();
        var inFormulary = await db.FormularyItems.Where(f => ids.Contains(f.ProductId)).Select(f => f.ProductId).ToListAsync(ct);
        if (inFormulary.Count != ids.Count) throw BusinessException.Validation("Only formulary items can be indented. Ask the pharmacy to add the item first.");
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        var ind = new WardIndent
        {
            BranchId = branch.Id, DepartmentId = dept.Id, Priority = i.Priority, EncounterRef = i.EncounterRef?.Trim(), Notes = i.Notes?.Trim(),
            RequestedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            IndentNo = SequenceService.Format(branch.Code, "G", Today, await sequences.NextAsync(branch.Id, "G", Today, ct)),
        };
        ind.Lines = i.Lines.Select(l => new WardIndentLine { WardIndentId = ind.Id, ProductId = l.ProductId, QtyUnits = l.QtyUnits }).ToList();
        db.WardIndents.Add(ind);
        audit.Record("ward_indent.created", nameof(WardIndent), ind.Id, $"{ind.IndentNo} {dept.Code} {i.Priority}");
        await db.SaveChangesAsync(ct);
        return await IndentDtoAsync(ind.Id, ct);
    }

    public async Task<List<WardIndentDto>> IndentsAsync(WardIndentStatus? status, Guid? departmentId, CancellationToken ct = default)
    {
        var q = db.WardIndents.AsNoTracking();
        if (IsWard) { var mine = await MyDepartmentAsync(ct); q = q.Where(x => x.DepartmentId == mine); }
        if (departmentId is { } d) q = q.Where(x => x.DepartmentId == d);
        q = status is { } s ? q.Where(x => x.Status == s) : q.Where(x => x.CreatedAt >= clock.UtcNow.AddDays(-30) || x.Status == WardIndentStatus.Open || x.Status == WardIndentStatus.PartlyIssued);
        var ids = await q.OrderByDescending(x => x.Priority).ThenBy(x => x.CreatedAt).Select(x => x.Id).Take(300).ToListAsync(ct);
        var list = new List<WardIndentDto>();
        foreach (var id in ids) list.Add(await IndentDtoAsync(id, ct));
        return list;
    }

    public async Task<WardIndentDto> GetIndentAsync(Guid id, CancellationToken ct = default)
    {
        var ind = await db.WardIndents.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Indent");
        await EnsureDepartmentAccessAsync(ind.DepartmentId, ct);
        return await IndentDtoAsync(id, ct);
    }

    public async Task<WardIndentDto> CancelIndentAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var ind = await db.WardIndents.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Indent");
        await EnsureDepartmentAccessAsync(ind.DepartmentId, ct);
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        if (ind.Status is WardIndentStatus.Closed or WardIndentStatus.Cancelled) throw BusinessException.Conflict("invalid_state", "This indent is already closed.");
        if (await db.WardIssues.AnyAsync(x => x.WardIndentId == id && x.Status == WardIssueStatus.PendingCheck, ct))
            throw BusinessException.Conflict("issue_pending", "An issue for this indent is waiting for its second check. Cancel that first.");
        ind.Status = ind.Lines.Any(l => l.IssuedUnits > 0) ? WardIndentStatus.Closed : WardIndentStatus.Cancelled; // part-issued indents close short
        ind.CancelReason = reason.Trim(); ind.ClosedAt = clock.UtcNow;
        audit.Record("ward_indent.closed", nameof(WardIndent), ind.Id, reason);
        await db.SaveChangesAsync(ct);
        return await IndentDtoAsync(id, ct);
    }

    private async Task<WardIndentDto> IndentDtoAsync(Guid id, CancellationToken ct)
    {
        var x = await db.WardIndents.AsNoTracking().Include(i => i.Department).Include(i => i.Lines).ThenInclude(l => l.Product).SingleAsync(i => i.Id == id, ct);
        var productIds = x.Lines.Select(l => l.ProductId).ToList();
        var stock = await StockAsync(productIds, ct);
        var by = x.RequestedByUserId is { } u ? await db.Users.Where(y => y.Id == u).Select(y => y.DisplayName).FirstOrDefaultAsync(ct) : null;
        return new WardIndentDto(x.Id, x.IndentNo, x.DepartmentId, x.Department!.Name, x.Status, x.Priority, x.EncounterRef, x.Notes, by, x.CreatedAt, x.FirstIssuedAt,
            x.CancelReason, x.Version, x.Lines.OrderBy(l => l.Product!.Name).Select(l => new WardIndentLineDto(l.Id, l.ProductId, l.Product!.Name, l.Product.BaseUnit,
                l.QtyUnits, l.IssuedUnits, x.Status is WardIndentStatus.Open or WardIndentStatus.PartlyIssued ? l.QtyUnits - l.IssuedUnits : 0, stock.GetValueOrDefault(l.ProductId))).ToList());
    }

    private async Task<Dictionary<Guid, decimal>> StockAsync(List<Guid> productIds, CancellationToken ct)
    {
        var today = Today;
        return await db.StockBalances.Where(b => b.BranchId == ctx.BranchId && productIds.Contains(b.ProductId) && b.QtyUnits > 0 && b.Batch!.ExpiryDate >= today && !b.Batch.IsBlocked)
            .GroupBy(b => b.ProductId).Select(g => new { g.Key, Q = g.Sum(b => b.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
    }

    // ===================== issues =====================

    public async Task<WardIssueDto> PrepareIssueAsync(WardIssueInput i, CancellationToken ct = default)
    {
        var dept = await db.Departments.SingleOrDefaultAsync(d => d.Id == i.DepartmentId && d.IsActive, ct) ?? throw BusinessException.NotFound("Department");
        var lines = i.Lines.Where(l => l.QtyUnits != 0).ToList();
        Require(lines.Count is > 0 and <= 100, "Add 1–100 items.");
        Require(lines.All(l => l.QtyUnits > 0 && l.QtyUnits == Math.Floor(l.QtyUnits)), "Quantities must be whole units.");
        WardIndent? indent = null;
        if (i.WardIndentId is { } indId)
        {
            indent = await db.WardIndents.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == indId, ct) ?? throw BusinessException.NotFound("Indent");
            Require(indent.DepartmentId == dept.Id, "The indent belongs to another department.");
            if (indent.Status is not (WardIndentStatus.Open or WardIndentStatus.PartlyIssued)) throw BusinessException.Conflict("invalid_state", "This indent is closed.");
            Require(lines.All(l => l.WardIndentLineId is { } lid && indent.Lines.Any(x => x.Id == lid && x.ProductId == l.ProductId)), "Each line must match an item on the indent.");
        }
        else Require(lines.All(l => l.WardIndentLineId is null), "Indent lines need the indent.");
        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        Require(products.Count == ids.Count, "Unknown product.");
        var highAlert = await db.FormularyItems.Where(f => ids.Contains(f.ProductId) && f.IsHighAlert).Select(f => f.ProductId).ToListAsync(ct);
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var issue = new WardIssue
        {
            BranchId = branch.Id, DepartmentId = dept.Id, WardIndentId = indent?.Id, ReceivedBy = i.ReceivedBy?.Trim(), PreparedByUserId = ctx.UserId, PreparedAt = clock.UtcNow,
            NeedsSecondCheck = ids.Any(p => highAlert.Contains(p) || ControlledSchedule(products[p].Schedule)),
            IssueNo = SequenceService.Format(branch.Code, "U", Today, await sequences.NextAsync(branch.Id, "U", Today, ct)),
        };
        issue.Status = issue.NeedsSecondCheck ? WardIssueStatus.PendingCheck : WardIssueStatus.Issued;
        issue.Lines = lines.Select(l => new WardIssueLine { WardIssueId = issue.Id, ProductId = l.ProductId, QtyUnits = l.QtyUnits, WardIndentLineId = l.WardIndentLineId }).ToList();
        db.WardIssues.Add(issue);
        if (indent is not null) CheckAgainstIndent(indent, issue);
        if (!issue.NeedsSecondCheck) await ReleaseAsync(issue, indent, products, ct);
        audit.Record(issue.NeedsSecondCheck ? "ward_issue.prepared" : "ward_issue.issued", nameof(WardIssue), issue.Id,
            $"{issue.IssueNo} to {dept.Code}{(issue.NeedsSecondCheck ? " — awaiting second check" : "")}");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await IssueDtoAsync(issue.Id, ct);
    }

    /// <summary>Second pharmacist verifies a high-alert / controlled issue; only then does stock move.</summary>
    public async Task<WardIssueDto> VerifyIssueAsync(Guid id, int version, CancellationToken ct = default)
    {
        var issue = await db.WardIssues.Include(x => x.Lines).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Issue");
        if (issue.Status != WardIssueStatus.PendingCheck) throw BusinessException.Conflict("invalid_state", "This issue is not waiting for a check.");
        if (issue.PreparedByUserId == ctx.UserId) throw BusinessException.Conflict("same_person", "A different pharmacist must do the second check.");
        db.Entry(issue).Property(x => x.Version).OriginalValue = version;
        var ids = issue.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        WardIndent? indent = issue.WardIndentId is { } indId ? await db.WardIndents.Include(x => x.Lines).SingleAsync(x => x.Id == indId, ct) : null;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (indent is not null)
        {
            if (indent.Status is not (WardIndentStatus.Open or WardIndentStatus.PartlyIssued)) throw BusinessException.Conflict("invalid_state", "The indent was closed meanwhile.");
            CheckAgainstIndent(indent, issue);
        }
        issue.Status = WardIssueStatus.Issued;
        issue.CheckedByUserId = ctx.UserId;
        await ReleaseAsync(issue, indent, products, ct);
        audit.Record("ward_issue.verified", nameof(WardIssue), issue.Id, $"{issue.IssueNo} second check");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return await IssueDtoAsync(issue.Id, ct);
    }

    public async Task<WardIssueDto> CancelIssueAsync(Guid id, string? reason, CancellationToken ct = default)
    {
        var issue = await db.WardIssues.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Issue");
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        if (issue.Status != WardIssueStatus.PendingCheck) throw BusinessException.Conflict("invalid_state", "Only issues waiting for a check can be cancelled; use a ward return for issued goods.");
        issue.Status = WardIssueStatus.Cancelled; issue.CancelReason = reason.Trim();
        audit.Record("ward_issue.cancelled", nameof(WardIssue), issue.Id, reason);
        await SaveAsync(ct);
        return await IssueDtoAsync(id, ct);
    }

    private static void CheckAgainstIndent(WardIndent indent, WardIssue issue)
    {
        foreach (var l in issue.Lines)
        {
            var il = indent.Lines.Single(x => x.Id == l.WardIndentLineId);
            if (l.QtyUnits > il.QtyUnits - il.IssuedUnits)
                throw BusinessException.Conflict("over_issue", $"Only {il.QtyUnits - il.IssuedUnits:0} unit(s) are still pending on the indent for this item.");
        }
    }

    private async Task ReleaseAsync(WardIssue issue, WardIndent? indent, Dictionary<Guid, Product> products, CancellationToken ct)
    {
        var today = Today;
        foreach (var l in issue.Lines)
        {
            var p = products[l.ProductId];
            var batches = await db.StockBalances.Where(s => s.BranchId == issue.BranchId && s.ProductId == p.Id && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
                .OrderBy(s => s.Batch!.ExpiryDate).ThenBy(s => s.Batch!.BatchNo).Select(s => new { s.Batch, s.QtyUnits }).ToListAsync(ct);
            if (batches.Sum(b => b.QtyUnits) < l.QtyUnits)
                throw BusinessException.Conflict("insufficient_stock", $"Only {batches.Sum(b => b.QtyUnits):0} {p.BaseUnit} of {p.Name} in stock (need {l.QtyUnits:0}).");
            var left = l.QtyUnits;
            foreach (var b in batches)
            {
                if (left <= 0) break;
                var take = Math.Min(left, b.QtyUnits);
                left -= take;
                var row = new WardIssueBatch { WardIssueLineId = l.Id, BatchId = b.Batch!.Id, QtyUnits = take, CostPerUnit = b.Batch.CostPerUnit };
                l.Batches.Add(row);
                db.WardIssueBatches.Add(row);
                await inventory.ApplyAsync(issue.BranchId, b.Batch, -take, MovementType.WardIssue, nameof(WardIssue), issue.Id, l.Id, $"to dept {issue.DepartmentId}", ct);
                issue.CostValue += Money.R2(take * b.Batch.CostPerUnit);
            }
            if (indent is not null) indent.Lines.Single(x => x.Id == l.WardIndentLineId).IssuedUnits += l.QtyUnits;
        }
        issue.IssuedAt = clock.UtcNow;
        if (indent is not null)
        {
            indent.FirstIssuedAt ??= issue.IssuedAt;
            var allDone = indent.Lines.All(x => x.IssuedUnits >= x.QtyUnits);
            indent.Status = allDone ? WardIndentStatus.Closed : WardIndentStatus.PartlyIssued;
            if (allDone) indent.ClosedAt = issue.IssuedAt;
            db.Entry(indent).Property(x => x.Status).IsModified = true; // bumps the indent's version: two pharmacists cannot over-issue it
        }
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "Someone else changed this at the same time. Reload and try again."); }
    }

    public async Task<List<WardIssueDto>> IssuesAsync(WardIssueStatus? status, Guid? departmentId, CancellationToken ct = default)
    {
        var q = db.WardIssues.AsNoTracking();
        if (IsWard) { var mine = await MyDepartmentAsync(ct); q = q.Where(x => x.DepartmentId == mine); }
        if (departmentId is { } d) q = q.Where(x => x.DepartmentId == d);
        q = status is { } s ? q.Where(x => x.Status == s) : q.Where(x => x.PreparedAt >= clock.UtcNow.AddDays(-30) || x.Status == WardIssueStatus.PendingCheck);
        var ids = await q.OrderByDescending(x => x.PreparedAt).Select(x => x.Id).Take(300).ToListAsync(ct);
        var list = new List<WardIssueDto>();
        foreach (var id in ids) list.Add(await IssueDtoAsync(id, ct));
        return list;
    }

    public async Task<WardIssueDto> GetIssueAsync(Guid id, CancellationToken ct = default)
    {
        var x = await db.WardIssues.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct) ?? throw BusinessException.NotFound("Issue");
        await EnsureDepartmentAccessAsync(x.DepartmentId, ct);
        return await IssueDtoAsync(id, ct);
    }

    private async Task<WardIssueDto> IssueDtoAsync(Guid id, CancellationToken ct)
    {
        var x = await db.WardIssues.AsNoTracking().Include(i => i.Department).Include(i => i.Lines).ThenInclude(l => l.Product)
            .Include(i => i.Lines).ThenInclude(l => l.Batches).ThenInclude(b => b.Batch).AsSplitQuery().SingleAsync(i => i.Id == id, ct);
        var ids = x.Lines.Select(l => l.ProductId).ToList();
        var high = await db.FormularyItems.Where(f => ids.Contains(f.ProductId) && f.IsHighAlert).Select(f => f.ProductId).ToListAsync(ct);
        var userIds = new[] { x.PreparedByUserId }.Concat(x.CheckedByUserId is { } c ? new[] { c } : []).ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        var indentNo = x.WardIndentId is { } ind ? await db.WardIndents.Where(i => i.Id == ind).Select(i => i.IndentNo).FirstOrDefaultAsync(ct) : null;
        return new WardIssueDto(x.Id, x.IssueNo, x.DepartmentId, x.Department!.Name, indentNo, x.Status, x.NeedsSecondCheck, names.GetValueOrDefault(x.PreparedByUserId, ""),
            x.PreparedAt, x.CheckedByUserId is { } cb ? names.GetValueOrDefault(cb) : null, x.IssuedAt, x.ReceivedBy, x.CostValue, x.CancelReason, x.Version,
            x.Lines.OrderBy(l => l.Product!.Name).Select(l => new WardIssueLineDto(l.Id, l.ProductId, l.Product!.Name, l.Product.BaseUnit, l.QtyUnits,
                high.Contains(l.ProductId) || ControlledSchedule(l.Product.Schedule),
                l.Batches.Select(b => new WardIssueBatchDto(b.Id, b.Batch!.BatchNo, b.Batch.ExpiryDate, b.QtyUnits, b.ReturnedUnits, b.CostPerUnit)).ToList())).ToList());
    }

    // ===================== returns =====================

    public async Task<WardReturnDto> CreateReturnAsync(WardReturnInput i, CancellationToken ct = default)
    {
        Require(Enum.IsDefined(i.Reason), "Invalid reason.");
        var issue = await db.WardIssues.Include(x => x.Department).Include(x => x.Lines).ThenInclude(l => l.Batches).ThenInclude(b => b.Batch)
            .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == i.WardIssueId, ct) ?? throw BusinessException.NotFound("Issue");
        if (issue.Status != WardIssueStatus.Issued) throw BusinessException.Conflict("invalid_state", "Only issued goods can come back.");
        var lines = i.Lines.Where(l => l.QtyUnits != 0).ToList();
        Require(lines.Count > 0, "Enter what came back.");
        Require(lines.All(l => l.QtyUnits > 0 && l.QtyUnits == Math.Floor(l.QtyUnits)), "Quantities must be whole units.");
        var all = issue.Lines.SelectMany(l => l.Batches.Select(b => (Line: l, Row: b))).ToDictionary(x => x.Row.Id);
        var branch = await db.Branches.SingleAsync(b => b.Id == issue.BranchId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var ret = new WardReturn
        {
            BranchId = issue.BranchId, DepartmentId = issue.DepartmentId, WardIssueId = issue.Id, Reason = i.Reason, Notes = i.Notes?.Trim(),
            CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            ReturnNo = SequenceService.Format(branch.Code, "V", Today, await sequences.NextAsync(branch.Id, "V", Today, ct)),
        };
        foreach (var l in lines)
        {
            if (!all.TryGetValue(l.WardIssueBatchId, out var src)) throw BusinessException.Validation("A returned line does not belong to this issue.");
            if (l.QtyUnits > src.Row.QtyUnits - src.Row.ReturnedUnits)
                throw BusinessException.Conflict("over_return", $"Only {src.Row.QtyUnits - src.Row.ReturnedUnits:0} unit(s) of batch {src.Row.Batch!.BatchNo} can still come back.");
            if (i.Reason == WardReturnReason.Unused && (src.Row.Batch!.ExpiryDate < Today || src.Row.Batch.IsBlocked))
                throw BusinessException.Conflict("not_reusable", $"Batch {src.Row.Batch.BatchNo} is expired or blocked; return it as Expired/Damaged.");
            src.Row.ReturnedUnits += l.QtyUnits;
            var rl = new WardReturnLine { WardReturnId = ret.Id, WardIssueBatchId = src.Row.Id, ProductId = src.Line.ProductId, BatchId = src.Row.BatchId, QtyUnits = l.QtyUnits, CostPerUnit = src.Row.CostPerUnit };
            ret.Lines.Add(rl);
            ret.CostValue += Money.R2(l.QtyUnits * src.Row.CostPerUnit);
            if (i.Reason == WardReturnReason.Unused)
                await inventory.ApplyAsync(issue.BranchId, src.Row.Batch!, l.QtyUnits, MovementType.WardReturnIn, nameof(WardReturn), ret.Id, rl.Id, $"from {issue.Department!.Code}", ct);
        }
        db.WardReturns.Add(ret);
        db.Entry(issue).Property(x => x.Status).IsModified = true; // version bump guards concurrent returns of the same lines
        audit.Record("ward_return.created", nameof(WardReturn), ret.Id, $"{ret.ReturnNo} {i.Reason} against {issue.IssueNo}");
        await SaveAsync(ct);
        await tx.CommitAsync(ct);
        return new WardReturnDto(ret.Id, ret.ReturnNo, issue.IssueNo, issue.Department!.Name, ret.Reason, ret.CostValue, ret.CreatedAt);
    }

    public async Task<List<WardReturnDto>> ReturnsAsync(CancellationToken ct = default)
    {
        var q = db.WardReturns.AsNoTracking();
        if (IsWard) { var mine = await MyDepartmentAsync(ct); q = q.Where(x => x.DepartmentId == mine); }
        return await q.OrderByDescending(r => r.CreatedAt).Take(300)
            .Select(r => new WardReturnDto(r.Id, r.ReturnNo, db.WardIssues.Where(x => x.Id == r.WardIssueId).Select(x => x.IssueNo).First(),
                db.Departments.Where(d => d.Id == r.DepartmentId).Select(d => d.Name).First(), r.Reason, r.CostValue, r.CreatedAt)).ToListAsync(ct);
    }

    // ===================== consumption =====================

    public async Task<ConsumptionReport> ConsumptionAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(to >= from && to.DayNumber - from.DayNumber <= 366, "Choose a range of up to a year.");
        var start = BusinessCalendar.StartOfDayUtc(from); var end = BusinessCalendar.StartOfDayUtc(to.AddDays(1));
        var issues = await db.WardIssues.AsNoTracking().Where(x => x.Status == WardIssueStatus.Issued && x.IssuedAt >= start && x.IssuedAt < end)
            .GroupBy(x => x.DepartmentId).Select(g => new { g.Key, N = g.Count(), V = g.Sum(x => x.CostValue) }).ToListAsync(ct);
        var returns = await db.WardReturns.AsNoTracking().Where(x => x.Reason == WardReturnReason.Unused && x.CreatedAt >= start && x.CreatedAt < end)
            .GroupBy(x => x.DepartmentId).Select(g => new { g.Key, V = g.Sum(x => x.CostValue) }).ToListAsync(ct);
        var open = await db.WardIndents.AsNoTracking().Where(x => x.Status == WardIndentStatus.Open || x.Status == WardIndentStatus.PartlyIssued)
            .GroupBy(x => x.DepartmentId).Select(g => new { g.Key, N = g.Count() }).ToListAsync(ct);
        var tat = (await db.WardIndents.AsNoTracking().Where(x => x.FirstIssuedAt != null && x.CreatedAt >= start && x.CreatedAt < end)
            .Select(x => new { x.DepartmentId, x.CreatedAt, x.FirstIssuedAt }).ToListAsync(ct))
            .GroupBy(x => x.DepartmentId).ToDictionary(g => g.Key, g => Math.Round((decimal)g.Average(x => (x.FirstIssuedAt!.Value - x.CreatedAt).TotalHours), 1));
        var depts = await db.Departments.AsNoTracking().OrderBy(d => d.Name).ToListAsync(ct);
        var rows = depts.Select(d =>
        {
            var iss = issues.FirstOrDefault(x => x.Key == d.Id); var ret = returns.FirstOrDefault(x => x.Key == d.Id)?.V ?? 0;
            return new DepartmentConsumption(d.Id, d.Name, iss?.N ?? 0, iss?.V ?? 0, ret, (iss?.V ?? 0) - ret, open.FirstOrDefault(x => x.Key == d.Id)?.N ?? 0,
                tat.TryGetValue(d.Id, out var h) ? h : null);
        }).Where(r => r.Issues > 0 || r.OpenIndents > 0 || r.ReturnedValue > 0).ToList();

        var issued = await db.WardIssueBatches.AsNoTracking()
            .Join(db.WardIssueLines, b => b.WardIssueLineId, l => l.Id, (b, l) => new { b, l })
            .Join(db.WardIssues.Where(x => x.Status == WardIssueStatus.Issued && x.IssuedAt >= start && x.IssuedAt < end), x => x.l.WardIssueId, i => i.Id, (x, i) => x)
            .GroupBy(x => x.l.ProductId).Select(g => new { g.Key, U = g.Sum(x => x.b.QtyUnits), V = g.Sum(x => x.b.QtyUnits * x.b.CostPerUnit) }).ToListAsync(ct);
        var back = await db.WardReturnLines.AsNoTracking()
            .Join(db.WardReturns.Where(r => r.Reason == WardReturnReason.Unused && r.CreatedAt >= start && r.CreatedAt < end), l => l.WardReturnId, r => r.Id, (l, r) => l)
            .GroupBy(l => l.ProductId).Select(g => new { g.Key, U = g.Sum(l => l.QtyUnits), V = g.Sum(l => l.QtyUnits * l.CostPerUnit) }).ToListAsync(ct);
        var pids = issued.Select(x => x.Key).ToList();
        var pn = await db.Products.Where(p => pids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => (p.Name, p.BaseUnit), ct);
        var top = issued.Select(x =>
        {
            var b = back.FirstOrDefault(r => r.Key == x.Key);
            return new ProductConsumption(x.Key, pn[x.Key].Name, pn[x.Key].BaseUnit, x.U - (b?.U ?? 0), Money.R2(x.V - (b?.V ?? 0)));
        }).OrderByDescending(x => x.Value).Take(20).ToList();
        return new ConsumptionReport(rows, top, rows.Sum(r => r.NetValue));
    }
}
