using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record ExpenseInput(DateOnly Date, ExpenseCategory Category, decimal Amount, decimal? Km, string? Note);
public record ExpenseDto(Guid Id, Guid RepUserId, string RepName, DateOnly Date, ExpenseCategory Category, decimal Amount, decimal? Km, string? Note, SfaStatus Status,
    string? ReviewedBy, string? ReviewComment);
public record TargetInput(Guid RepUserId, DateOnly Month, int CallsTarget, decimal CoverageTargetPercent);
public record ScorecardRow(Guid RepUserId, string RepName, int CallsTarget, int Calls, decimal CoverageTargetPercent, decimal CoveragePercent,
    decimal ExpensesApproved, decimal ExpensesPending, bool CallsMet, bool CoverageMet);

public partial class SfaService
{
    public const int ExpenseWindowDays = 31;

    public async Task<ExpenseDto> ClaimExpenseAsync(ExpenseInput i, CancellationToken ct = default)
    {
        Require(IsRep, "Only medical representatives claim field expenses.");
        Require(Enum.IsDefined(i.Category), "Invalid category.");
        Require(i.Amount > 0 && i.Amount <= 100000, "Amount must be between ₹0 and ₹1,00,000.");
        Require(i.Km is null or (> 0 and <= 2000), "Kilometres look wrong.");
        if (i.Date > Today) throw BusinessException.Conflict("future_date", "Expenses cannot be claimed for a future day.");
        if (i.Date < Today.AddDays(-ExpenseWindowDays)) throw BusinessException.Conflict("outside_window", $"Claims must be filed within {ExpenseWindowDays} days.");
        var e = new ExpenseClaim { RepUserId = ctx.UserId, Date = i.Date, Category = i.Category, Amount = Money.R2(i.Amount), Km = i.Km, Note = i.Note?.Trim(), CreatedAt = clock.UtcNow };
        db.ExpenseClaims.Add(e);
        audit.Record("expense.claimed", nameof(ExpenseClaim), e.Id, $"{i.Category} {e.Amount}");
        await db.SaveChangesAsync(ct);
        return (await ExpenseDtosAsync(db.ExpenseClaims.Where(x => x.Id == e.Id), ct)).Single();
    }

    public async Task<List<ExpenseDto>> ExpensesAsync(DateOnly month, SfaStatus? status, CancellationToken ct = default)
    {
        var reps = await VisibleRepsAsync(ct);
        var m = MonthStart(month); var end = m.AddMonths(1);
        var q = db.ExpenseClaims.Where(e => reps.Contains(e.RepUserId) && e.Date >= m && e.Date < end);
        if (status is { } s) q = q.Where(e => e.Status == s);
        return await ExpenseDtosAsync(q, ct);
    }

    public async Task<ExpenseDto> ReviewExpenseAsync(Guid id, bool approve, string? comment, CancellationToken ct = default)
    {
        var e = await db.ExpenseClaims.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Expense");
        await EnsureRepVisibleAsync(e.RepUserId, ct);
        await EnsureCanSuperviseAsync(e.RepUserId, ct);
        if (e.Status != SfaStatus.Submitted) throw BusinessException.Conflict("invalid_state", "Already reviewed.");
        Require(approve || !string.IsNullOrWhiteSpace(comment), "Give the reason for rejecting.");
        e.Status = approve ? SfaStatus.Approved : SfaStatus.Rejected;
        e.ReviewedByUserId = ctx.UserId; e.ReviewedAt = clock.UtcNow; e.ReviewComment = comment?.Trim();
        audit.Record(approve ? "expense.approved" : "expense.rejected", nameof(ExpenseClaim), e.Id, comment);
        await db.SaveChangesAsync(ct);
        return (await ExpenseDtosAsync(db.ExpenseClaims.Where(x => x.Id == e.Id), ct)).Single();
    }

    private async Task<List<ExpenseDto>> ExpenseDtosAsync(IQueryable<ExpenseClaim> q, CancellationToken ct)
    {
        var rows = await q.AsNoTracking().OrderByDescending(e => e.Date).ThenBy(e => e.CreatedAt).Take(1000).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.RepUserId).Concat(rows.Where(r => r.ReviewedByUserId != null).Select(r => r.ReviewedByUserId!.Value)), ct);
        return rows.Select(e => new ExpenseDto(e.Id, e.RepUserId, names.GetValueOrDefault(e.RepUserId, ""), e.Date, e.Category, e.Amount, e.Km, e.Note, e.Status,
            e.ReviewedByUserId is { } r ? names.GetValueOrDefault(r) : null, e.ReviewComment)).ToList();
    }

    public async Task SetTargetAsync(TargetInput i, CancellationToken ct = default)
    {
        Require(i.CallsTarget is >= 0 and <= 2000 && i.CoverageTargetPercent is >= 0 and <= 100, "Targets are out of range.");
        if (!await db.Users.AnyAsync(u => u.Id == i.RepUserId && u.Role == UserRole.MedicalRep, ct)) throw BusinessException.NotFound("Representative");
        await EnsureCanSuperviseAsync(i.RepUserId, ct);
        var m = MonthStart(i.Month);
        var t = await db.SfaTargets.SingleOrDefaultAsync(x => x.RepUserId == i.RepUserId && x.Month == m, ct);
        t ??= db.SfaTargets.Add(new SfaTarget { RepUserId = i.RepUserId, Month = m }).Entity;
        t.CallsTarget = i.CallsTarget; t.CoverageTargetPercent = i.CoverageTargetPercent;
        audit.Record("sfa_target.set", nameof(SfaTarget), t.Id, $"{m:yyyy-MM}: {i.CallsTarget} calls, {i.CoverageTargetPercent}% coverage");
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Target vs achievement for the month, with expense totals.</summary>
    public async Task<List<ScorecardRow>> ScorecardAsync(DateOnly month, CancellationToken ct = default)
    {
        var m = MonthStart(month); var end = m.AddMonths(1);
        var coverage = await CoverageAsync(m, ct);
        var reps = coverage.Select(c => c.RepUserId).ToList();
        var targets = await db.SfaTargets.AsNoTracking().Where(t => reps.Contains(t.RepUserId) && t.Month == m).ToDictionaryAsync(t => t.RepUserId, ct);
        var exp = await db.ExpenseClaims.AsNoTracking().Where(e => reps.Contains(e.RepUserId) && e.Date >= m && e.Date < end)
            .GroupBy(e => e.RepUserId).Select(g => new
            {
                g.Key,
                Approved = g.Where(e => e.Status == SfaStatus.Approved).Sum(e => e.Amount),
                Pending = g.Where(e => e.Status == SfaStatus.Submitted).Sum(e => e.Amount),
            }).ToDictionaryAsync(x => x.Key, ct);
        return coverage.Select(c =>
        {
            var t = targets.GetValueOrDefault(c.RepUserId);
            var e = exp.GetValueOrDefault(c.RepUserId);
            return new ScorecardRow(c.RepUserId, c.RepName, t?.CallsTarget ?? 0, c.Visits, t?.CoverageTargetPercent ?? 0, c.CoveragePercent,
                e?.Approved ?? 0, e?.Pending ?? 0, t is not null && c.Visits >= t.CallsTarget, t is not null && c.CoveragePercent >= t.CoverageTargetPercent);
        }).ToList();
    }
}
