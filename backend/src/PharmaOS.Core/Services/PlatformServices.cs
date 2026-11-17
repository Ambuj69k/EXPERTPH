using System.IO.Compression;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

/// <param name="PriceNote">Pricing is a HYPOTHESIS to be validated with customers — not a published price.</param>
public record PlanDef(string Code, string Name, string PriceNote, decimal? MonthlyPriceHypothesis, int? MaxBranches, int? MaxUsers, string[] Modules);

public static class Plans
{
    public const string Crm = "crm", Purchasing = "purchasing", Accounting = "accounting", MultiStore = "multistore";
    /// <summary>Online ordering & delivery: in NO plan by default — enabled per customer (extra module) only after a compliance review.</summary>
    public const string Online = "online";
    public static readonly string[] AllModules = [Crm, Purchasing, Accounting, MultiStore, Online];
    public const string TrialPlan = "chain";
    public const int TrialDays = 30;

    public static readonly PlanDef[] Catalog =
    [
        new("single", "Single store", "Pricing hypothesis — validate with pilots", 999m, 1, 5, [Crm, Purchasing]),
        new("chain", "Small chain", "Pricing hypothesis — validate with pilots", 2499m, 10, 30, [Crm, Purchasing, Accounting, MultiStore]),
        new("enterprise", "Enterprise", "Quoted per customer", null, null, null, [Crm, Purchasing, Accounting, MultiStore]),
    ];

    public static PlanDef Get(string code) => Catalog.FirstOrDefault(p => p.Code == code) ?? Catalog[0];
}

public record Entitlements(string PlanCode, string PlanName, SubscriptionStatus Status, bool ReadOnly, string? Notice, DateTimeOffset? TrialEndsAt,
    DateOnly? PaidUntil, string[] Modules, int? MaxBranches, int? MaxUsers, int Branches, int Users, DateTimeOffset? SupportAccessUntil,
    DateTimeOffset? DeletionScheduledAt);

/// <summary>Tenant-side view and enforcement of the subscription (plan modules, limits, read-only state).</summary>
public class SubscriptionService(PharmaDbContext db, ITenantContext ctx, IClock clock)
{
    private Entitlements? _cached;

    public static async Task<Subscription> GetOrCreateAsync(PharmaDbContext db, Guid tenantId, DateTimeOffset now, CancellationToken ct)
    {
        var s = await db.Subscriptions.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (s is not null) return s;
        var created = await db.Tenants.Where(t => t.Id == tenantId).Select(t => t.CreatedAt).SingleAsync(ct);
        s = new Subscription { TenantId = tenantId, PlanCode = Plans.TrialPlan, Status = SubscriptionStatus.Trial, TrialEndsAt = created.AddDays(Plans.TrialDays), UpdatedAt = now };
        db.Subscriptions.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public static string[] ModulesFor(Subscription s)
    {
        var plan = Plans.Get(s.PlanCode);
        var extra = (s.ExtraModules ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var disabled = (s.DisabledModules ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return plan.Modules.Union(extra).Except(disabled).Where(Plans.AllModules.Contains).Order().ToArray();
    }

    public async Task<Entitlements> CurrentAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;
        var now = clock.UtcNow;
        var s = await GetOrCreateAsync(db, ctx.TenantId, now, ct);
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == ctx.TenantId, ct);
        var plan = Plans.Get(s.PlanCode);
        string? notice = null;
        var readOnly = false;
        if (t.DeletionScheduledAt is not null) { readOnly = true; notice = $"Account scheduled for deletion on {t.DeletionScheduledAt:dd MMM yyyy}. Export your data."; }
        else if (s.Status == SubscriptionStatus.Cancelled) { readOnly = true; notice = "Subscription cancelled — account is read-only. You can still export your data."; }
        else if (s.Status == SubscriptionStatus.Trial && s.TrialEndsAt < now) { readOnly = true; notice = "Trial ended — account is read-only until a plan is activated. Data export remains available."; }
        else if (s.Status == SubscriptionStatus.Trial) notice = $"Trial ends {s.TrialEndsAt:dd MMM yyyy}.";
        else if (s.Status == SubscriptionStatus.PastDue) notice = "Payment overdue — please clear the pending invoice to avoid interruption.";
        return _cached = new Entitlements(plan.Code, plan.Name, s.Status, readOnly, notice, s.TrialEndsAt, s.PaidUntil, ModulesFor(s),
            s.MaxBranchesOverride ?? plan.MaxBranches, s.MaxUsersOverride ?? plan.MaxUsers,
            await db.Branches.CountAsync(b => b.IsActive, ct), await db.Users.CountAsync(u => u.IsActive, ct), t.SupportAccessUntil, t.DeletionScheduledAt);
    }

    public async Task EnsureModuleAsync(string module, CancellationToken ct = default)
    {
        if (!(await CurrentAsync(ct)).Modules.Contains(module))
            throw new BusinessException("module_not_in_plan", $"This feature ({module}) is not included in your plan.", 403);
    }

    public async Task EnsureCanAddBranchAsync(CancellationToken ct = default)
    {
        var e = await CurrentAsync(ct);
        if (e.MaxBranches is { } max && e.Branches >= max) throw BusinessException.Conflict("plan_limit", $"Your plan allows {max} branch(es).");
    }

    public async Task EnsureCanAddUserAsync(CancellationToken ct = default)
    {
        var e = await CurrentAsync(ct);
        if (e.MaxUsers is { } max && e.Users >= max) throw BusinessException.Conflict("plan_limit", $"Your plan allows {max} user(s).");
    }
}

// ===================== tenant-side support & export =====================

public record TicketInput(string Subject, string Body, string? Priority);
public record ReplyInput(string Body);
public record TicketDto(Guid Id, Guid TenantId, string? TenantName, string Subject, TicketStatus Status, string Priority, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, List<TicketMessageDto> Messages);
public record TicketMessageDto(bool FromProvider, string AuthorName, string Body, DateTimeOffset At);
public record SupportAccessRequest(int Hours);

public class TenantSupportService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit)
{
    public async Task<DateTimeOffset?> SetSupportAccessAsync(int hours, CancellationToken ct = default)
    {
        Require(hours is >= 0 and <= 72, "Access can be granted for up to 72 hours (0 revokes).");
        var t = await db.Tenants.SingleAsync(x => x.Id == ctx.TenantId, ct);
        t.SupportAccessUntil = hours == 0 ? null : clock.UtcNow.AddHours(hours);
        audit.Record(hours == 0 ? "support_access.revoked" : "support_access.granted", nameof(Tenant), t.Id, hours == 0 ? null : $"{hours}h");
        await db.SaveChangesAsync(ct);
        return t.SupportAccessUntil;
    }

    public async Task<TicketDto> CreateTicketAsync(TicketInput r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.Subject) && r.Subject.Length <= 200, "Subject is required.");
        Require(!string.IsNullOrWhiteSpace(r.Body) && r.Body.Length <= 4000, "Describe the problem (max 4000 characters).");
        var user = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct) ?? "Staff";
        var now = clock.UtcNow;
        var t = new SupportTicket { Subject = r.Subject.Trim(), Priority = r.Priority is "high" or "low" ? r.Priority : "normal", CreatedByUserId = ctx.UserId, CreatedAt = now, UpdatedAt = now };
        t.Messages.Add(new SupportMessage { SupportTicketId = t.Id, AuthorName = user, Body = r.Body.Trim(), At = now });
        db.SupportTickets.Add(t);
        await db.SaveChangesAsync(ct);
        return await GetAsync(db, t.Id, ct);
    }

    public async Task<TicketDto> ReplyAsync(Guid id, ReplyInput r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.Body) && r.Body.Length <= 4000, "Message is required.");
        var t = await db.SupportTickets.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Ticket");
        var user = await db.Users.Where(u => u.Id == ctx.UserId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct) ?? "Staff";
        db.SupportMessages.Add(new SupportMessage { SupportTicketId = t.Id, AuthorName = user, Body = r.Body.Trim(), At = clock.UtcNow });
        t.Status = TicketStatus.Open;
        t.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(db, id, ct);
    }

    public async Task<List<TicketDto>> ListAsync(CancellationToken ct = default)
    {
        var ids = await db.SupportTickets.OrderByDescending(t => t.UpdatedAt).Take(100).Select(t => t.Id).ToListAsync(ct);
        var list = new List<TicketDto>();
        foreach (var id in ids) list.Add(await GetAsync(db, id, ct));
        return list;
    }

    public static async Task<TicketDto> GetAsync(PharmaDbContext db, Guid id, CancellationToken ct, bool platform = false)
    {
        var q = platform ? db.SupportTickets.IgnoreQueryFilters() : db.SupportTickets;
        var t = await q.AsNoTracking().Include(x => x.Messages).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Ticket");
        var tenantName = platform ? await db.Tenants.Where(x => x.Id == t.TenantId).Select(x => x.Name).SingleAsync(ct) : null;
        return new TicketDto(t.Id, t.TenantId, tenantName, t.Subject, t.Status, t.Priority, t.CreatedAt, t.UpdatedAt,
            t.Messages.OrderBy(m => m.At).Select(m => new TicketMessageDto(m.FromProvider, m.AuthorName, m.Body, m.At)).ToList());
    }
}

/// <summary>Owner's full data export: a ZIP of CSV files (customer data ownership / offboarding).</summary>
public class ExportService(PharmaDbContext db, AuditService audit)
{
    public async Task<byte[]> ExportAllAsync(CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            async Task Add<T>(string name, IQueryable<T> q) => Write(zip, name, await q.ToListAsync(ct));
            await Add("branches.csv", db.Branches.AsNoTracking());
            await Add("users.csv", db.Users.AsNoTracking().Select(u => new { u.Id, u.Email, u.DisplayName, u.Role, u.DefaultBranchId, u.IsActive, u.CreatedAt }));
            await Add("products.csv", db.Products.AsNoTracking());
            await Add("suppliers.csv", db.Suppliers.AsNoTracking().Select(s => new { s.Id, s.Name, s.Gstin, s.Phone, s.Email, s.Address, s.DrugLicenseNo, s.PaymentTermsDays, s.IsActive }));
            await Add("customers.csv", db.Customers.AsNoTracking());
            await Add("consents.csv", db.ConsentRecords.AsNoTracking());
            await Add("batches.csv", db.Batches.AsNoTracking().Select(b => new { b.Id, b.ProductId, b.BatchNo, b.ExpiryDate, b.Mrp, b.PurchaseRate, b.CostPerUnit, b.IsBlocked, b.BlockReason }));
            await Add("stock_balances.csv", db.StockBalances.AsNoTracking().Select(s => new { s.BranchId, s.ProductId, s.BatchId, s.QtyUnits }));
            await Add("stock_movements.csv", db.StockMovements.AsNoTracking());
            await Add("sales_invoices.csv", db.SalesInvoices.AsNoTracking());
            await Add("sales_invoice_lines.csv", db.SalesInvoiceLines.AsNoTracking());
            await Add("sales_payments.csv", db.SalesPayments.AsNoTracking());
            await Add("sales_returns.csv", db.SalesReturns.AsNoTracking());
            await Add("sales_return_lines.csv", db.SalesReturnLines.AsNoTracking());
            await Add("purchase_invoices.csv", db.PurchaseInvoices.AsNoTracking());
            await Add("purchase_invoice_lines.csv", db.PurchaseInvoiceLines.AsNoTracking());
            await Add("purchase_returns.csv", db.PurchaseReturns.AsNoTracking());
            await Add("purchase_return_lines.csv", db.PurchaseReturnLines.AsNoTracking());
            await Add("supplier_payments.csv", db.SupplierPayments.AsNoTracking());
            await Add("customer_receipts.csv", db.CustomerReceipts.AsNoTracking());
            await Add("loyalty_entries.csv", db.LoyaltyEntries.AsNoTracking());
            await Add("refill_reminders.csv", db.RefillReminders.AsNoTracking());
            await Add("ledger_accounts.csv", db.LedgerAccounts.AsNoTracking());
            await Add("journal_entries.csv", db.JournalEntries.AsNoTracking());
            await Add("journal_lines.csv", db.JournalLines.AsNoTracking());
            await Add("audit_events.csv", db.AuditEvents.AsNoTracking());
            var readme = zip.CreateEntry("README.txt");
            await using var w = new StreamWriter(readme.Open(), new UTF8Encoding(false));
            await w.WriteAsync("PharmaOS data export. One CSV per table, UTF-8, comma separated, first row = column names. " +
                               "Ids link tables (e.g. sales_invoice_lines.SalesInvoiceId → sales_invoices.Id). Quantities are in base units; money in INR.");
        }
        audit.Record("data.exported", "Tenant", null, "full export");
        await db.SaveChangesAsync(ct);
        return ms.ToArray();
    }

    private static void Write<T>(ZipArchive zip, string name, List<T> rows)
    {
        var props = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => IsSimple(Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType)).ToArray();
        var entry = zip.CreateEntry(name);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(string.Join(",", props.Select(p => p.Name)) + "\r\n");
        foreach (var r in rows)
            w.Write(string.Join(",", props.Select(p => Csv(p.GetValue(r)))) + "\r\n");
    }

    private static bool IsSimple(Type t) => t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal) || t == typeof(Guid)
                                            || t == typeof(DateOnly) || t == typeof(DateTimeOffset) || t == typeof(DateTime);

    private static string Csv(object? v)
    {
        var s = v switch
        {
            null => "",
            DateTimeOffset d => d.ToString("O"),
            DateOnly d => d.ToString("yyyy-MM-dd"),
            decimal d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => v.ToString() ?? "",
        };
        return s.Contains(',') || s.Contains('"') || s.Contains('\n') || s.Contains('\r') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}

// ===================== provider side =====================

public record PlatformSession(Guid UserId, string Name, string Email);
public record TenantRow(Guid Id, string Name, bool IsActive, DateTimeOffset CreatedAt, string PlanCode, SubscriptionStatus Status, DateTimeOffset? TrialEndsAt,
    DateOnly? PaidUntil, int Branches, int Users, int BillsLast30Days, DateTimeOffset? LastBillAt, decimal Overdue, int OpenTickets, MigrationStatus Migration,
    DateTimeOffset? DeletionScheduledAt, DateTimeOffset? DeletedAt);
public record SubscriptionInput(string PlanCode, SubscriptionStatus Status, DateTimeOffset? TrialEndsAt, string? ExtraModules, string? DisabledModules,
    int? MaxBranchesOverride, int? MaxUsersOverride, MigrationStatus Migration, string? Notes);
public record PlatformInvoiceInput(DateOnly PeriodFrom, DateOnly PeriodTo, decimal Amount, DateOnly DueDate);
public record PlatformInvoiceDto(Guid Id, Guid TenantId, string TenantName, string InvoiceNo, DateOnly PeriodFrom, DateOnly PeriodTo, decimal Amount,
    DateOnly DueDate, PlatformInvoiceStatus Status, DateTimeOffset? PaidAt, string? PaymentReference, int DaysOverdue);
public record TenantDetail(TenantRow Tenant, SubscriptionInput Subscription, string[] Modules, DateTimeOffset? SupportAccessUntil,
    List<PlatformInvoiceDto> Invoices, List<TicketDto> Tickets, List<AuditRow> RecentAudit);
public record AuditRow(DateTimeOffset At, string Action, string? Summary);
public record SupportSession(Guid TenantId, Guid BranchId, string BranchName, string BranchCode, string TenantName, DateTimeOffset ExpiresAt);
public record ScheduleDeletionRequest(int DaysFromNow);
public record PlatformDashboard(int Tenants, int Active, int Trials, int TrialsExpired, int PastDue, int Suspended, decimal OverdueAmount, int OpenTickets,
    int BillsLast24h, List<PlanDef> Plans);

/// <summary>SaaS provider administration. Every query here crosses tenants deliberately (IgnoreQueryFilters).</summary>
public class PlatformService(PharmaDbContext db, IClock clock, ISecretProtector protector)
{
    private static readonly PasswordHasher<PlatformUser> Hasher = new();

    public async Task EnsureBootstrapAsync(string? email, string? password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || await db.PlatformUsers.AnyAsync(ct)) return;
        var u = new PlatformUser { Email = email.Trim().ToLowerInvariant(), Name = "Platform admin", CreatedAt = clock.UtcNow };
        u.PasswordHash = Hasher.HashPassword(u, password);
        db.PlatformUsers.Add(u);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Password step; returns the session, or the user id that must complete the MFA code step.</summary>
    public async Task<(PlatformSession? Session, Guid? MfaUserId)> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        var u = await db.PlatformUsers.SingleOrDefaultAsync(x => x.Email == (email ?? "").Trim().ToLowerInvariant() && x.IsActive, ct)
                ?? throw new BusinessException("invalid_credentials", "Email or password is incorrect.", 401);
        Locked(u);
        if (Hasher.VerifyHashedPassword(u, u.PasswordHash, password ?? "") == PasswordVerificationResult.Failed)
        {
            await FailAsync(u, ct);
            throw new BusinessException("invalid_credentials", "Email or password is incorrect.", 401);
        }
        if (u.MfaEnabled) return (null, u.Id);
        return (await SucceedAsync(u, ct), null);
    }

    public async Task<PlatformSession> CompleteMfaAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var u = await db.PlatformUsers.SingleOrDefaultAsync(x => x.Id == userId && x.IsActive && x.MfaEnabled, ct)
                ?? throw new BusinessException("invalid_credentials", "Sign in again.", 401);
        Locked(u);
        if (!Totp.Verify(protector.Unprotect(u.MfaSecret!), code, clock.UtcNow))
        {
            await FailAsync(u, ct);
            throw new BusinessException("invalid_code", "That code is not correct.", 401);
        }
        return await SucceedAsync(u, ct);
    }

    public async Task<MfaSetup> BeginMfaSetupAsync(Guid userId, CancellationToken ct = default)
    {
        var u = await db.PlatformUsers.SingleAsync(x => x.Id == userId, ct);
        if (u.MfaEnabled) throw BusinessException.Conflict("mfa_on", "Two-step sign-in is already on.");
        var secret = Totp.NewSecret();
        u.MfaSecret = protector.Protect(secret);
        await db.SaveChangesAsync(ct);
        return new MfaSetup(Totp.Base32(secret), Totp.Uri("PharmaOS Provider", u.Email, secret));
    }

    public async Task EnableMfaAsync(Guid userId, string code, CancellationToken ct = default)
    {
        var u = await db.PlatformUsers.SingleAsync(x => x.Id == userId, ct);
        Require(u.MfaSecret is not null && !u.MfaEnabled, "Start setup first.");
        if (!Totp.Verify(protector.Unprotect(u.MfaSecret!), code, clock.UtcNow)) throw BusinessException.Validation("That code is not correct.");
        u.MfaEnabled = true;
        await db.SaveChangesAsync(ct);
    }

    public async Task<bool> MfaEnabledAsync(Guid userId, CancellationToken ct = default) =>
        await db.PlatformUsers.Where(x => x.Id == userId).Select(x => x.MfaEnabled).SingleAsync(ct);

    private void Locked(PlatformUser u)
    {
        if (u.LockedUntil > clock.UtcNow) throw new BusinessException("locked", "Too many failed attempts. Try again later.", 423);
    }

    private async Task FailAsync(PlatformUser u, CancellationToken ct)
    {
        if (++u.FailedLogins >= 5) { u.LockedUntil = clock.UtcNow.AddMinutes(15); u.FailedLogins = 0; }
        await db.SaveChangesAsync(ct);
    }

    private async Task<PlatformSession> SucceedAsync(PlatformUser u, CancellationToken ct)
    {
        u.FailedLogins = 0; u.LockedUntil = null;
        await db.SaveChangesAsync(ct);
        return new PlatformSession(u.Id, u.Name, u.Email);
    }

    public async Task<PlatformSession> LoginlessSessionAsync(Guid userId, CancellationToken ct = default) =>
        await db.PlatformUsers.Where(u => u.Id == userId).Select(u => new PlatformSession(u.Id, u.Name, u.Email)).SingleAsync(ct);

    private void Audit(Guid tenantId, Guid platformUserId, string action, string? summary) =>
        db.AuditEvents.Add(new AuditEvent { TenantId = tenantId, UserId = platformUserId, Action = "platform." + action, EntityType = nameof(Tenant), EntityId = tenantId, Summary = summary, At = clock.UtcNow });

    public async Task<PlatformDashboard> DashboardAsync(CancellationToken ct = default)
    {
        var rows = await TenantsAsync(ct);
        var now = clock.UtcNow;
        var since = now.AddHours(-24);
        return new PlatformDashboard(rows.Count, rows.Count(r => r.Status == SubscriptionStatus.Active), rows.Count(r => r.Status == SubscriptionStatus.Trial && r.TrialEndsAt >= now),
            rows.Count(r => r.Status == SubscriptionStatus.Trial && r.TrialEndsAt < now), rows.Count(r => r.Status == SubscriptionStatus.PastDue),
            rows.Count(r => !r.IsActive && r.DeletedAt == null), rows.Sum(r => r.Overdue), rows.Sum(r => r.OpenTickets),
            await db.SalesInvoices.IgnoreQueryFilters().CountAsync(s => s.InvoiceAt >= since, ct), Plans.Catalog.ToList());
    }

    public async Task<List<TenantRow>> TenantsAsync(CancellationToken ct = default)
    {
        var tenants = await db.Tenants.AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        foreach (var t in tenants.Where(t => t.DeletedAt is null)) await SubscriptionService.GetOrCreateAsync(db, t.Id, clock.UtcNow, ct);
        var subs = await db.Subscriptions.AsNoTracking().ToDictionaryAsync(s => s.TenantId, ct);
        var since = clock.UtcNow.AddDays(-30);
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var bills = await db.SalesInvoices.IgnoreQueryFilters().Where(s => s.InvoiceAt >= since).GroupBy(s => s.TenantId)
            .Select(g => new { g.Key, Count = g.Count(), Last = g.Max(x => x.InvoiceAt) }).ToDictionaryAsync(x => x.Key, ct);
        var users = await db.Users.IgnoreQueryFilters().Where(u => u.IsActive).GroupBy(u => u.TenantId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var branches = await db.Branches.IgnoreQueryFilters().Where(b => b.IsActive).GroupBy(b => b.TenantId).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        var overdue = await db.PlatformInvoices.Where(i => i.Status == PlatformInvoiceStatus.Due && i.DueDate < today).GroupBy(i => i.TenantId)
            .Select(g => new { g.Key, A = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.A, ct);
        var tickets = await db.SupportTickets.IgnoreQueryFilters().Where(t => t.Status != TicketStatus.Closed).GroupBy(t => t.TenantId)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C, ct);
        return tenants.Select(t =>
        {
            subs.TryGetValue(t.Id, out var s);
            bills.TryGetValue(t.Id, out var b);
            return new TenantRow(t.Id, t.Name, t.IsActive, t.CreatedAt, s?.PlanCode ?? "-", s?.Status ?? SubscriptionStatus.Cancelled, s?.TrialEndsAt, s?.PaidUntil,
                branches.GetValueOrDefault(t.Id), users.GetValueOrDefault(t.Id), b?.Count ?? 0, b?.Last, overdue.GetValueOrDefault(t.Id),
                tickets.GetValueOrDefault(t.Id), s?.Migration ?? MigrationStatus.NotStarted, t.DeletionScheduledAt, t.DeletedAt);
        }).ToList();
    }

    public async Task<TenantDetail> TenantAsync(Guid tenantId, CancellationToken ct = default)
    {
        var row = (await TenantsAsync(ct)).SingleOrDefault(t => t.Id == tenantId) ?? throw BusinessException.NotFound("Tenant");
        var s = await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == tenantId, ct);
        var ticketIds = await db.SupportTickets.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).OrderByDescending(x => x.UpdatedAt).Take(20).Select(x => x.Id).ToListAsync(ct);
        var tickets = new List<TicketDto>();
        foreach (var id in ticketIds) tickets.Add(await TenantSupportService.GetAsync(db, id, ct, platform: true));
        var audit = await db.AuditEvents.IgnoreQueryFilters().AsNoTracking().Where(a => a.TenantId == tenantId && (a.Action.StartsWith("platform.") || a.Action.StartsWith("support")))
            .OrderByDescending(a => a.At).Take(50).Select(a => new AuditRow(a.At, a.Action, a.Summary)).ToListAsync(ct);
        return new TenantDetail(row,
            s is null ? new SubscriptionInput("-", SubscriptionStatus.Cancelled, null, null, null, null, null, MigrationStatus.NotStarted, null)
                : new SubscriptionInput(s.PlanCode, s.Status, s.TrialEndsAt, s.ExtraModules, s.DisabledModules, s.MaxBranchesOverride, s.MaxUsersOverride, s.Migration, s.Notes),
            s is null ? [] : SubscriptionService.ModulesFor(s), t.SupportAccessUntil, await InvoicesAsync(tenantId, false, ct), tickets, audit);
    }

    public async Task<TenantDetail> UpdateSubscriptionAsync(Guid tenantId, Guid by, SubscriptionInput i, CancellationToken ct = default)
    {
        Require(Plans.Catalog.Any(p => p.Code == i.PlanCode), "Unknown plan.");
        Require(Enum.IsDefined(i.Status) && Enum.IsDefined(i.Migration), "Invalid status.");
        string? Clean(string? m) => m is null ? null : string.Join(",", m.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(Plans.AllModules.Contains));
        var s = await SubscriptionService.GetOrCreateAsync(db, tenantId, clock.UtcNow, ct);
        s.PlanCode = i.PlanCode; s.Status = i.Status; s.TrialEndsAt = i.TrialEndsAt; s.ExtraModules = Clean(i.ExtraModules); s.DisabledModules = Clean(i.DisabledModules);
        s.MaxBranchesOverride = i.MaxBranchesOverride; s.MaxUsersOverride = i.MaxUsersOverride; s.Migration = i.Migration; s.Notes = i.Notes; s.UpdatedAt = clock.UtcNow;
        Audit(tenantId, by, "subscription_updated", $"{i.PlanCode}/{i.Status}");
        await db.SaveChangesAsync(ct);
        return await TenantAsync(tenantId, ct);
    }

    public async Task SetActiveAsync(Guid tenantId, Guid by, bool active, CancellationToken ct = default)
    {
        var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.DeletedAt == null, ct) ?? throw BusinessException.NotFound("Tenant");
        t.IsActive = active;
        Audit(tenantId, by, active ? "reactivated" : "suspended", null);
        await db.SaveChangesAsync(ct);
    }

    public async Task<PlatformInvoiceDto> CreateInvoiceAsync(Guid tenantId, Guid by, PlatformInvoiceInput i, CancellationToken ct = default)
    {
        Require(i.Amount > 0 && i.PeriodFrom <= i.PeriodTo, "Invalid amount or period.");
        if (!await db.Tenants.AnyAsync(t => t.Id == tenantId, ct)) throw BusinessException.NotFound("Tenant");
        var inv = new PlatformInvoice
        {
            TenantId = tenantId, PeriodFrom = i.PeriodFrom, PeriodTo = i.PeriodTo, Amount = Money.R2(i.Amount), DueDate = i.DueDate, CreatedAt = clock.UtcNow,
            InvoiceNo = $"PO-{clock.UtcNow:yyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
        };
        db.PlatformInvoices.Add(inv);
        Audit(tenantId, by, "invoice_created", $"{inv.InvoiceNo} {inv.Amount}");
        await db.SaveChangesAsync(ct);
        return (await InvoicesAsync(tenantId, false, ct)).Single(x => x.Id == inv.Id);
    }

    public async Task<PlatformInvoiceDto> MarkPaidAsync(Guid invoiceId, Guid by, string reference, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reference), "Payment reference is required.");
        var inv = await db.PlatformInvoices.SingleOrDefaultAsync(x => x.Id == invoiceId, ct) ?? throw BusinessException.NotFound("Invoice");
        if (inv.Status != PlatformInvoiceStatus.Due) throw BusinessException.Conflict("not_due", "Invoice is not due.");
        inv.Status = PlatformInvoiceStatus.Paid; inv.PaidAt = clock.UtcNow; inv.PaymentReference = reference.Trim();
        var s = await SubscriptionService.GetOrCreateAsync(db, inv.TenantId, clock.UtcNow, ct);
        if (s.PaidUntil is null || s.PaidUntil < inv.PeriodTo) s.PaidUntil = inv.PeriodTo;
        if (s.Status is SubscriptionStatus.Trial or SubscriptionStatus.PastDue) s.Status = SubscriptionStatus.Active;
        s.UpdatedAt = clock.UtcNow;
        Audit(inv.TenantId, by, "invoice_paid", $"{inv.InvoiceNo} ref {reference}");
        await db.SaveChangesAsync(ct);
        return (await InvoicesAsync(inv.TenantId, false, ct)).Single(x => x.Id == inv.Id);
    }

    public async Task VoidInvoiceAsync(Guid invoiceId, Guid by, CancellationToken ct = default)
    {
        var inv = await db.PlatformInvoices.SingleOrDefaultAsync(x => x.Id == invoiceId, ct) ?? throw BusinessException.NotFound("Invoice");
        if (inv.Status != PlatformInvoiceStatus.Due) throw BusinessException.Conflict("not_due", "Only due invoices can be voided.");
        inv.Status = PlatformInvoiceStatus.Void;
        Audit(inv.TenantId, by, "invoice_voided", inv.InvoiceNo);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Invoices for a tenant, or (overdueOnly) every due invoice past its due date — the dunning list.</summary>
    public async Task<List<PlatformInvoiceDto>> InvoicesAsync(Guid? tenantId, bool overdueOnly, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var q = db.PlatformInvoices.AsNoTracking().AsQueryable();
        if (tenantId is not null) q = q.Where(i => i.TenantId == tenantId);
        if (overdueOnly) q = q.Where(i => i.Status == PlatformInvoiceStatus.Due && i.DueDate < today);
        var rows = await q.OrderByDescending(i => i.CreatedAt).Take(500)
            .Join(db.Tenants, i => i.TenantId, t => t.Id, (i, t) => new { i, t.Name }).ToListAsync(ct);
        return rows.Select(x => new PlatformInvoiceDto(x.i.Id, x.i.TenantId, x.Name, x.i.InvoiceNo, x.i.PeriodFrom, x.i.PeriodTo, x.i.Amount, x.i.DueDate, x.i.Status,
            x.i.PaidAt, x.i.PaymentReference, x.i.Status == PlatformInvoiceStatus.Due ? Math.Max(0, today.DayNumber - x.i.DueDate.DayNumber) : 0)).ToList();
    }

    public async Task<List<TicketDto>> OpenTicketsAsync(CancellationToken ct = default)
    {
        var ids = await db.SupportTickets.IgnoreQueryFilters().Where(t => t.Status != TicketStatus.Closed).OrderBy(t => t.UpdatedAt).Take(200).Select(t => t.Id).ToListAsync(ct);
        var list = new List<TicketDto>();
        foreach (var id in ids) list.Add(await TenantSupportService.GetAsync(db, id, ct, platform: true));
        return list;
    }

    public async Task<TicketDto> ReplyAsync(Guid ticketId, PlatformSession by, ReplyInput r, TicketStatus status, CancellationToken ct = default)
    {
        var t = await db.SupportTickets.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.Id == ticketId, ct) ?? throw BusinessException.NotFound("Ticket");
        if (!string.IsNullOrWhiteSpace(r.Body))
            db.SupportMessages.Add(new SupportMessage { TenantId = t.TenantId, SupportTicketId = t.Id, FromProvider = true, AuthorName = $"{by.Name} (PharmaOS support)", Body = r.Body.Trim(), At = clock.UtcNow });
        t.Status = status;
        t.UpdatedAt = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        return await TenantSupportService.GetAsync(db, ticketId, ct, platform: true);
    }

    /// <summary>Read-only support session — only inside the window the tenant owner granted. Every request is audited by the API.</summary>
    public async Task<SupportSession> StartSupportSessionAsync(Guid tenantId, Guid by, CancellationToken ct = default)
    {
        var t = await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == tenantId && x.DeletedAt == null, ct) ?? throw BusinessException.NotFound("Tenant");
        if (t.SupportAccessUntil is null || t.SupportAccessUntil <= clock.UtcNow)
            throw BusinessException.Forbidden("The customer has not granted support access. Ask the owner to allow it from Settings.");
        var branch = await db.Branches.IgnoreQueryFilters().Where(b => b.TenantId == tenantId && b.IsActive).OrderBy(b => b.IsWarehouse).FirstAsync(ct);
        Audit(tenantId, by, "support_session_started", null);
        await db.SaveChangesAsync(ct);
        var expires = new[] { t.SupportAccessUntil.Value, clock.UtcNow.AddHours(2) }.Min();
        return new SupportSession(tenantId, branch.Id, branch.Name, branch.Code, t.Name, expires);
    }

    public async Task ScheduleDeletionAsync(Guid tenantId, Guid by, int days, CancellationToken ct = default)
    {
        Require(days is >= 0 and <= 90, "Deletion can be scheduled 0-90 days ahead.");
        var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.DeletedAt == null, ct) ?? throw BusinessException.NotFound("Tenant");
        t.DeletionScheduledAt = clock.UtcNow.AddDays(days);
        var s = await SubscriptionService.GetOrCreateAsync(db, tenantId, clock.UtcNow, ct);
        s.Status = SubscriptionStatus.Cancelled;
        Audit(tenantId, by, "deletion_scheduled", $"{t.DeletionScheduledAt:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
    }

    public async Task CancelDeletionAsync(Guid tenantId, Guid by, CancellationToken ct = default)
    {
        var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.DeletedAt == null, ct) ?? throw BusinessException.NotFound("Tenant");
        t.DeletionScheduledAt = null;
        Audit(tenantId, by, "deletion_cancelled", null);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Irreversibly deletes all of a tenant's business data once the scheduled date has passed.
    /// The tenant row is kept as an anonymised tombstone; provider invoices are kept as the provider's own records.
    /// </summary>
    public async Task PurgeAsync(Guid tenantId, Guid by, CancellationToken ct = default)
    {
        var t = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.DeletedAt == null, ct) ?? throw BusinessException.NotFound("Tenant");
        if (t.DeletionScheduledAt is null || t.DeletionScheduledAt > clock.UtcNow)
            throw BusinessException.Conflict("not_scheduled", "Deletion must be scheduled and its date reached before purging.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        async Task Del<T>(IQueryable<T> set) where T : TenantEntity => await set.IgnoreQueryFilters().Where(x => x.TenantId == tenantId).ExecuteDeleteAsync(ct);
        await Del(db.SupportMessages); await Del(db.SupportTickets);
        await Del(db.JournalLines); await Del(db.JournalEntries);
        await Del(db.LoyaltyEntries); await Del(db.NotificationLogs); await Del(db.ConsentRecords); await Del(db.RefillReminders); await Del(db.NotificationTemplates);
        await Del(db.CustomerReceiptAllocations); await Del(db.CustomerReceipts); await Del(db.SupplierPaymentAllocations); await Del(db.SupplierPayments);
        await Del(db.SalesReturnLines); await Del(db.SalesReturns); await Del(db.SalesPayments); await Del(db.SalesInvoiceLines); await Del(db.SalesInvoices);
        await Del(db.StockMovements); await Del(db.StockBalances); await Del(db.StockAdjustmentLines); await Del(db.StockAdjustments);
        await Del(db.StockCountLines); await Del(db.StockCounts);
        await Del(db.StockTransferLines); await Del(db.StockTransfers); await Del(db.StockIndentLines); await Del(db.StockIndents);
        await Del(db.PurchaseReturnLines); await Del(db.PurchaseReturns); await Del(db.SupplierProductMaps);
        await Del(db.PurchaseInvoiceLines); await Del(db.PurchaseInvoices); await Del(db.PurchaseOrderLines); await Del(db.PurchaseOrders);
        await Del(db.CashMovements); await Del(db.CashShifts);
        await Del(db.LedgerAccounts); await Del(db.Batches); await Del(db.Customers); await Del(db.Products); await Del(db.Suppliers);
        await Del(db.DocumentSequences); await Del(db.AuditEvents);
        await Del(db.Users); await Del(db.Branches);
        t.Name = "Deleted tenant"; t.IsActive = false; t.DeletedAt = clock.UtcNow; t.SupportAccessUntil = null;
        Audit(tenantId, by, "purged", null);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
