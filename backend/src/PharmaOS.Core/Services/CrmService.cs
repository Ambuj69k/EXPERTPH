using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record ConsentInput(ConsentChannel Channel, ConsentPurpose Purpose, bool Granted, string Source);
public record ConsentState(ConsentChannel Channel, ConsentPurpose Purpose, bool Granted, string Source, DateTimeOffset At);
public record CustomerProfile(Guid Id, string Name, string? Phone, decimal? CreditLimit, bool IsActive, Guid? MergedIntoId,
    DateTimeOffset CreatedAt, int BillCount, decimal TotalSpend, DateTimeOffset? FirstPurchaseAt, DateTimeOffset? LastPurchaseAt,
    decimal Balance, int LoyaltyPoints, decimal LoyaltyValue, List<ConsentState> Consents, int ActiveReminders);
public record PurchaseHistoryItem(Guid InvoiceId, string InvoiceNo, DateTimeOffset At, SalesStatus Status, decimal NetAmount,
    List<string> Items);

public record ReminderInput(Guid CustomerId, Guid ProductId, int IntervalDays, DateOnly? NextDueDate, string? Notes, bool CustomerRequested);
public record ReminderUpdate(int IntervalDays, DateOnly NextDueDate, ReminderStatus Status, string? Notes);
public record ReminderDto(Guid Id, Guid CustomerId, string CustomerName, string? CustomerPhone, Guid ProductId, string ProductName,
    int IntervalDays, DateOnly NextDueDate, int DaysUntilDue, ReminderStatus Status, string? Notes, DateTimeOffset? LastContactedAt,
    bool WhatsAppConsent, bool SmsConsent);
public record NotifyRequest(ConsentChannel Channel);
public record PreparedMessage(Guid LogId, NotificationStatus Status, string? Recipient, string Message, string? Reason, string? ClickToChatUrl);
public record NotificationDto(Guid Id, Guid? CustomerId, string? CustomerName, ConsentChannel Channel, ConsentPurpose Purpose, string TemplateKey,
    string? Recipient, string Message, NotificationStatus Status, string Provider, string? Error, DateTimeOffset CreatedAt, DateTimeOffset? SentAt);
public record TemplateDto(string Key, ConsentChannel Channel, string Body, bool IsActive, bool IsDefault);
public record TemplateInput(string Key, ConsentChannel Channel, string Body, bool IsActive);

public record LoyaltySettings(bool Enabled, decimal RupeesPerPoint, decimal PointValue, int MinRedeemPoints);
public record LoyaltyEntryDto(LoyaltyEntryType Type, int Points, string? InvoiceNo, string? Note, DateTimeOffset At);
public record LoyaltyAccount(Guid CustomerId, int Points, decimal Value, LoyaltySettings Settings, List<LoyaltyEntryDto> Entries);
public record AdjustPointsRequest(int Points, string Reason);

public record MergeRequest(Guid SourceCustomerId, Guid TargetCustomerId);
public record CustomerLookup(Guid Id, string Name, string? Phone, int LoyaltyPoints, decimal LoyaltyValue, decimal Balance, decimal? CreditLimit);

public record RetentionReport(DateOnly From, DateOnly To, int TotalBills, int IdentifiedBills, decimal IdentifiedSharePercent, int UniqueCustomers,
    int NewCustomers, int ReturningCustomers, int RepeatWithinPeriod, decimal RepeatRatePercent, List<TopCustomer> TopCustomers);
public record TopCustomer(Guid CustomerId, string Name, string? Phone, int Bills, decimal Spend);

/// <summary>Customer profile, consent, reminders, outbound message log and merge.</summary>
public partial class CrmService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit, LedgerService ledger, LoyaltyService loyalty)
{
    public static readonly Dictionary<string, string> DefaultTemplates = new()
    {
        ["refill_reminder"] = "Namaste {{customerName}}, as you asked, this is a reminder from {{businessName}} that your {{productName}} may be due around {{dueDate}}. Reply STOP to stop reminders.",
    };

    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);

    // ---------- profile ----------

    public async Task<CustomerProfile> ProfileAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Customer");
        var bills = db.SalesInvoices.AsNoTracking().Where(s => s.CustomerId == id && s.Status == SalesStatus.Posted);
        var stats = await bills.GroupBy(_ => 1).Select(g => new
        {
            Count = g.Count(), Spend = g.Sum(x => x.NetAmount), First = g.Min(x => x.InvoiceAt), Last = g.Max(x => x.InvoiceAt),
        }).SingleOrDefaultAsync(ct);
        var returned = await db.SalesReturns.Where(r => bills.Select(b => b.Id).Contains(r.SalesInvoiceId)).SumAsync(r => (decimal?)r.RefundAmount, ct) ?? 0;
        var points = await loyalty.BalanceAsync(id, ct);
        var settings = await loyalty.SettingsAsync(ct);
        return new CustomerProfile(c.Id, c.Name, c.Phone, c.CreditLimit, c.IsActive, c.MergedIntoId, c.CreatedAt, stats?.Count ?? 0,
            (stats?.Spend ?? 0) - returned, stats?.First, stats?.Last, await ledger.CustomerBalanceAsync(id, ct), points,
            Money.R2(points * settings.PointValue), await CurrentConsentsAsync(id, ct),
            await db.RefillReminders.CountAsync(r => r.CustomerId == id && r.Status == ReminderStatus.Active, ct));
    }

    /// <summary>Counter lookup by mobile: name, loyalty and dues in one call.</summary>
    public async Task<CustomerLookup?> LookupByPhoneAsync(string phone, CancellationToken ct = default)
    {
        string? normalized;
        try { normalized = MasterDataService.NormalizePhone(phone); } catch (BusinessException) { return null; }
        var c = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Phone == normalized && x.IsActive, ct);
        if (c is null) return null;
        var points = await loyalty.BalanceAsync(c.Id, ct);
        var s = await loyalty.SettingsAsync(ct);
        return new CustomerLookup(c.Id, c.Name, c.Phone, points, Money.R2(points * s.PointValue), await ledger.CustomerBalanceAsync(c.Id, ct), c.CreditLimit);
    }

    public async Task<PagedResult<PurchaseHistoryItem>> PurchasesAsync(Guid id, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.SalesInvoices.AsNoTracking().Where(s => s.CustomerId == id);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(s => s.InvoiceAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new { s.Id, s.InvoiceNo, s.InvoiceAt, s.Status, s.NetAmount, Items = s.Lines.Select(l => l.Product!.Name + " ×" + l.QtyUnits).ToList() })
            .ToListAsync(ct);
        return new PagedResult<PurchaseHistoryItem>(rows.Select(r => new PurchaseHistoryItem(r.Id, r.InvoiceNo, r.InvoiceAt, r.Status, r.NetAmount,
            r.Items.Select(i => i.Replace(".0000", "")).ToList())).ToList(), total, page, pageSize);
    }

    // ---------- consent ----------

    public async Task<List<ConsentState>> CurrentConsentsAsync(Guid customerId, CancellationToken ct = default)
    {
        var all = await db.ConsentRecords.AsNoTracking().Where(c => c.CustomerId == customerId).ToListAsync(ct);
        return all.GroupBy(c => (c.Channel, c.Purpose))
            .Select(g => g.OrderByDescending(x => x.At).First())
            .Select(c => new ConsentState(c.Channel, c.Purpose, c.Granted, c.Source, c.At))
            .OrderBy(c => c.Channel).ThenBy(c => c.Purpose).ToList();
    }

    public async Task<List<ConsentState>> RecordConsentAsync(Guid customerId, ConsentInput input, CancellationToken ct = default)
    {
        Require(Enum.IsDefined(input.Channel) && Enum.IsDefined(input.Purpose), "Invalid channel or purpose.");
        Require(!string.IsNullOrWhiteSpace(input.Source) && input.Source.Length <= 100, "Record how consent was given (e.g. 'verbal at counter').");
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct)) throw BusinessException.NotFound("Customer");
        db.ConsentRecords.Add(new ConsentRecord
        {
            CustomerId = customerId, Channel = input.Channel, Purpose = input.Purpose, Granted = input.Granted, Source = input.Source.Trim(),
            RecordedByUserId = ctx.UserId, At = clock.UtcNow,
        });
        audit.Record(input.Granted ? "consent.granted" : "consent.withdrawn", nameof(Customer), customerId, $"{input.Channel}/{input.Purpose} via {input.Source}");
        await db.SaveChangesAsync(ct);
        return await CurrentConsentsAsync(customerId, ct);
    }

    private async Task<bool> HasConsentAsync(Guid customerId, ConsentChannel channel, ConsentPurpose purpose, CancellationToken ct)
    {
        var latest = await db.ConsentRecords.AsNoTracking().Where(c => c.CustomerId == customerId && c.Channel == channel && c.Purpose == purpose)
            .OrderByDescending(c => c.At).Select(c => (bool?)c.Granted).FirstOrDefaultAsync(ct);
        return latest == true;
    }

    // ---------- reminders ----------

    public async Task<ReminderDto> CreateReminderAsync(ReminderInput r, CancellationToken ct = default)
    {
        Require(r.CustomerRequested, "Reminders can only be set when the customer asks for them.");
        Require(r.IntervalDays is >= 1 and <= 365, "Interval must be 1-365 days.");
        Require(r.Notes is null || r.Notes.Length <= 300, "Notes too long.");
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == r.CustomerId && c.IsActive, ct) ?? throw BusinessException.NotFound("Customer");
        Require(customer.Phone is not null, "Customer needs a mobile number for reminders.");
        if (!await db.Products.AnyAsync(p => p.Id == r.ProductId, ct)) throw BusinessException.NotFound("Product");
        if (await db.RefillReminders.AnyAsync(x => x.CustomerId == r.CustomerId && x.ProductId == r.ProductId && x.Status != ReminderStatus.Cancelled, ct))
            throw BusinessException.Conflict("duplicate_reminder", "A reminder for this product already exists for this customer.");
        var reminder = new RefillReminder
        {
            BranchId = ctx.BranchId, CustomerId = r.CustomerId, ProductId = r.ProductId, IntervalDays = r.IntervalDays,
            NextDueDate = r.NextDueDate ?? Today.AddDays(r.IntervalDays), Notes = r.Notes?.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
        };
        db.RefillReminders.Add(reminder);
        audit.Record("reminder.created", nameof(RefillReminder), reminder.Id, $"every {r.IntervalDays}d");
        await db.SaveChangesAsync(ct);
        return (await ListRemindersAsync(null, null, r.CustomerId, ct)).Single(x => x.Id == reminder.Id);
    }

    public async Task<ReminderDto> UpdateReminderAsync(Guid id, ReminderUpdate u, CancellationToken ct = default)
    {
        Require(u.IntervalDays is >= 1 and <= 365, "Interval must be 1-365 days.");
        Require(Enum.IsDefined(u.Status), "Invalid status.");
        var r = await db.RefillReminders.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Reminder");
        r.IntervalDays = u.IntervalDays;
        r.NextDueDate = u.NextDueDate;
        r.Status = u.Status;
        r.Notes = u.Notes?.Trim();
        audit.Record("reminder.updated", nameof(RefillReminder), r.Id, $"{u.Status} every {u.IntervalDays}d next {u.NextDueDate}");
        await db.SaveChangesAsync(ct);
        return (await ListRemindersAsync(null, null, r.CustomerId, ct)).Single(x => x.Id == id);
    }

    public async Task<List<ReminderDto>> ListRemindersAsync(int? dueWithinDays, ReminderStatus? status, Guid? customerId, CancellationToken ct = default)
    {
        var q = db.RefillReminders.AsNoTracking().Where(r => r.BranchId == ctx.BranchId);
        if (status is not null) q = q.Where(r => r.Status == status);
        if (customerId is not null) q = q.Where(r => r.CustomerId == customerId);
        if (dueWithinDays is not null) { var until = Today.AddDays(dueWithinDays.Value); q = q.Where(r => r.NextDueDate <= until); }
        var rows = await q.OrderBy(r => r.NextDueDate).Take(500)
            .Select(r => new { r, CustomerName = r.Customer!.Name, r.Customer.Phone, ProductName = r.Product!.Name }).ToListAsync(ct);
        var ids = rows.Select(x => x.r.CustomerId).Distinct().ToList();
        var consents = await db.ConsentRecords.AsNoTracking()
            .Where(c => ids.Contains(c.CustomerId) && c.Purpose == ConsentPurpose.Reminders).ToListAsync(ct);
        bool Has(Guid cid, ConsentChannel ch) => consents.Where(c => c.CustomerId == cid && c.Channel == ch).OrderByDescending(c => c.At).FirstOrDefault()?.Granted == true;
        var today = Today;
        return rows.Select(x => new ReminderDto(x.r.Id, x.r.CustomerId, x.CustomerName, x.Phone, x.r.ProductId, x.ProductName, x.r.IntervalDays,
            x.r.NextDueDate, x.r.NextDueDate.DayNumber - today.DayNumber, x.r.Status, x.r.Notes, x.r.LastContactedAt,
            Has(x.r.CustomerId, ConsentChannel.WhatsApp), Has(x.r.CustomerId, ConsentChannel.Sms))).ToList();
    }

    /// <summary>Called when a sale is posted: purchases reset the next due date for matching active reminders.</summary>
    public async Task OnSalePostedAsync(Guid customerId, IEnumerable<Guid> productIds, DateOnly businessDate, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var reminders = await db.RefillReminders.Where(r => r.CustomerId == customerId && ids.Contains(r.ProductId) && r.Status == ReminderStatus.Active).ToListAsync(ct);
        foreach (var r in reminders) r.NextDueDate = businessDate.AddDays(r.IntervalDays);
    }

    // ---------- notifications ----------

    /// <summary>
    /// Prepares a reminder message. Checks consent and opt-out first; a message without consent is logged as Skipped and never sent.
    /// No paid provider is used: WhatsApp messages are sent by staff via a click-to-chat link and then marked sent.
    /// </summary>
    public async Task<PreparedMessage> PrepareReminderAsync(Guid reminderId, NotifyRequest req, CancellationToken ct = default)
    {
        Require(req.Channel is ConsentChannel.WhatsApp or ConsentChannel.Sms, "Reminders can be sent by WhatsApp or SMS.");
        var r = await db.RefillReminders.Include(x => x.Customer).Include(x => x.Product)
                    .SingleOrDefaultAsync(x => x.Id == reminderId && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Reminder");
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        var body = await TemplateBodyAsync("refill_reminder", req.Channel, ct);
        var message = Render(body, new()
        {
            ["customerName"] = r.Customer!.Name, ["productName"] = r.Product!.Name, ["dueDate"] = r.NextDueDate.ToString("dd MMM yyyy"),
            ["businessName"] = tenant.Name,
        });

        string? reason = null;
        if (r.Status != ReminderStatus.Active) reason = "Reminder is not active.";
        else if (r.Customer.Phone is null) reason = "Customer has no mobile number.";
        else if (!await HasConsentAsync(r.CustomerId, req.Channel, ConsentPurpose.Reminders, ct)) reason = $"No {req.Channel} consent for reminders (or customer opted out).";

        var log = new NotificationLog
        {
            BranchId = ctx.BranchId, CustomerId = r.CustomerId, Channel = req.Channel, Purpose = ConsentPurpose.Reminders, TemplateKey = "refill_reminder",
            Recipient = r.Customer.Phone, Message = message, Status = reason is null ? NotificationStatus.Pending : NotificationStatus.Skipped,
            Error = reason, RelatedEntityType = nameof(RefillReminder), RelatedEntityId = r.Id, CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
        };
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
        var url = reason is null && req.Channel == ConsentChannel.WhatsApp
            ? $"https://wa.me/91{r.Customer.Phone}?text={Uri.EscapeDataString(message)}" : null;
        return new PreparedMessage(log.Id, log.Status, log.Recipient, message, reason, url);
    }

    public async Task<NotificationDto> MarkNotificationAsync(Guid id, bool sent, string? error, CancellationToken ct = default)
    {
        var log = await db.NotificationLogs.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Notification");
        if (log.Status != NotificationStatus.Pending) throw BusinessException.Conflict("not_pending", "Only pending messages can be updated.");
        log.Status = sent ? NotificationStatus.Sent : NotificationStatus.Failed;
        log.SentAt = sent ? clock.UtcNow : null;
        log.Error = sent ? null : (error ?? "Not delivered");
        if (sent && log.RelatedEntityType == nameof(RefillReminder))
        {
            var r = await db.RefillReminders.SingleOrDefaultAsync(x => x.Id == log.RelatedEntityId, ct);
            if (r is not null) r.LastContactedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);
        return (await ListNotificationsAsync(null, 1, 1, id, ct)).Items.Single();
    }

    public async Task<PagedResult<NotificationDto>> ListNotificationsAsync(Guid? customerId, int page, int pageSize, Guid? id = null, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.NotificationLogs.AsNoTracking().Where(n => n.BranchId == ctx.BranchId);
        if (customerId is not null) q = q.Where(n => n.CustomerId == customerId);
        if (id is not null) q = q.Where(n => n.Id == id);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(n => n.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .GroupJoin(db.Customers, n => n.CustomerId, c => c.Id, (n, cs) => new { n, cs })
            .SelectMany(x => x.cs.DefaultIfEmpty(), (x, c) => new NotificationDto(x.n.Id, x.n.CustomerId, c != null ? c.Name : null, x.n.Channel, x.n.Purpose,
                x.n.TemplateKey, x.n.Recipient, x.n.Message, x.n.Status, x.n.Provider, x.n.Error, x.n.CreatedAt, x.n.SentAt))
            .ToListAsync(ct);
        return new PagedResult<NotificationDto>(rows, total, page, pageSize);
    }

    public async Task<List<TemplateDto>> TemplatesAsync(CancellationToken ct = default)
    {
        var saved = await db.NotificationTemplates.AsNoTracking().ToListAsync(ct);
        var result = new List<TemplateDto>();
        foreach (var (key, body) in DefaultTemplates)
            foreach (var ch in new[] { ConsentChannel.WhatsApp, ConsentChannel.Sms })
            {
                var t = saved.SingleOrDefault(x => x.Key == key && x.Channel == ch);
                result.Add(t is null ? new TemplateDto(key, ch, body, true, true) : new TemplateDto(key, ch, t.Body, t.IsActive, false));
            }
        return result;
    }

    public async Task<TemplateDto> SaveTemplateAsync(TemplateInput input, CancellationToken ct = default)
    {
        Require(DefaultTemplates.ContainsKey(input.Key), "Unknown template key.");
        Require(!string.IsNullOrWhiteSpace(input.Body) && input.Body.Length <= 1000, "Template body is required (max 1000).");
        var unknown = PlaceholderRx().Matches(input.Body).Select(m => m.Groups[1].Value).Where(v => v is not ("customerName" or "productName" or "dueDate" or "businessName")).ToList();
        Require(unknown.Count == 0, $"Unknown placeholder(s): {string.Join(", ", unknown)}");
        var t = await db.NotificationTemplates.SingleOrDefaultAsync(x => x.Key == input.Key && x.Channel == input.Channel, ct);
        if (t is null) { t = new NotificationTemplate { Key = input.Key, Channel = input.Channel }; db.NotificationTemplates.Add(t); }
        t.Body = input.Body.Trim();
        t.IsActive = input.IsActive;
        audit.Record("template.saved", nameof(NotificationTemplate), t.Id, $"{input.Key}/{input.Channel}");
        await db.SaveChangesAsync(ct);
        return new TemplateDto(t.Key, t.Channel, t.Body, t.IsActive, false);
    }

    private async Task<string> TemplateBodyAsync(string key, ConsentChannel channel, CancellationToken ct)
    {
        var t = await db.NotificationTemplates.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key && x.Channel == channel && x.IsActive, ct);
        return t?.Body ?? DefaultTemplates[key];
    }

    private static string Render(string body, Dictionary<string, string> vars) =>
        PlaceholderRx().Replace(body, m => vars.TryGetValue(m.Groups[1].Value, out var v) ? v : m.Value);

    [GeneratedRegex(@"\{\{\s*(\w+)\s*\}\}")] private static partial Regex PlaceholderRx();

    // ---------- merge ----------

    /// <summary>
    /// Moves bills, receipts, reminders and messages from a duplicate record to the surviving one.
    /// Consent and loyalty history are immutable, so they are carried over as new entries on the target.
    /// The source is deactivated and keeps a pointer to the target.
    /// </summary>
    public async Task<CustomerProfile> MergeAsync(MergeRequest r, CancellationToken ct = default)
    {
        Require(r.SourceCustomerId != r.TargetCustomerId, "Choose two different customers.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var source = await db.Customers.SingleOrDefaultAsync(c => c.Id == r.SourceCustomerId && c.IsActive, ct) ?? throw BusinessException.NotFound("Duplicate customer");
        var target = await db.Customers.SingleOrDefaultAsync(c => c.Id == r.TargetCustomerId && c.IsActive, ct) ?? throw BusinessException.NotFound("Customer to keep");
        var sid = source.Id; var tid = target.Id;

        await db.SalesInvoices.Where(s => s.CustomerId == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, tid), ct);
        await db.CustomerReceipts.Where(s => s.CustomerId == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, tid), ct);
        await db.NotificationLogs.Where(s => s.CustomerId == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, tid), ct);
        var targetProducts = await db.RefillReminders.Where(x => x.CustomerId == tid && x.Status != ReminderStatus.Cancelled).Select(x => x.ProductId).ToListAsync(ct);
        await db.RefillReminders.Where(x => x.CustomerId == sid && targetProducts.Contains(x.ProductId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, ReminderStatus.Cancelled), ct); // avoid duplicate reminders
        await db.RefillReminders.Where(x => x.CustomerId == sid).ExecuteUpdateAsync(s => s.SetProperty(x => x.CustomerId, tid), ct);

        var now = clock.UtcNow;
        var targetConsents = await CurrentConsentsAsync(tid, ct);
        foreach (var c in await CurrentConsentsAsync(sid, ct))
        {
            // Keep the more recent decision for each channel/purpose (an opt-out on either record wins if newer).
            var existing = targetConsents.SingleOrDefault(t => t.Channel == c.Channel && t.Purpose == c.Purpose);
            if (existing is null || existing.At < c.At)
                db.ConsentRecords.Add(new ConsentRecord { CustomerId = tid, Channel = c.Channel, Purpose = c.Purpose, Granted = c.Granted,
                    Source = $"merged: {c.Source}"[..Math.Min(100, $"merged: {c.Source}".Length)], RecordedByUserId = ctx.UserId, At = now });
        }
        var points = await loyalty.BalanceAsync(sid, ct);
        if (points != 0)
        {
            db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = sid, Type = LoyaltyEntryType.Adjust, Points = -points, Note = "Merged into another record", CreatedAt = now, CreatedByUserId = ctx.UserId });
            db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = tid, Type = LoyaltyEntryType.Adjust, Points = points, Note = "Merged from duplicate record", CreatedAt = now, CreatedByUserId = ctx.UserId });
        }
        var phoneToMove = target.Phone is null ? source.Phone : null;
        source.Phone = null; // release the unique mobile number first
        source.IsActive = false;
        source.MergedIntoId = tid;
        if (target.CreditLimit is null && source.CreditLimit is not null) target.CreditLimit = source.CreditLimit;
        audit.Record("customer.merged", nameof(Customer), tid, $"{source.Name} ({sid}) merged into {target.Name}");
        await db.SaveChangesAsync(ct);
        if (phoneToMove is not null) { target.Phone = phoneToMove; await db.SaveChangesAsync(ct); }
        await tx.CommitAsync(ct);
        return await ProfileAsync(tid, ct);
    }

    // ---------- reporting ----------

    /// <summary>
    /// Bills = posted (non-cancelled) sales in the period, by business date.
    /// New customer = first-ever posted bill falls in the period; returning = had a posted bill before the period.
    /// Repeat within period = ≥2 bills in the period. Walk-in (no customer) bills are excluded from customer counts.
    /// </summary>
    public async Task<RetentionReport> RetentionAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(from <= to, "From must be before To.");
        var posted = db.SalesInvoices.AsNoTracking().Where(s => s.BranchId == ctx.BranchId && s.Status == SalesStatus.Posted);
        var inPeriod = posted.Where(s => s.BusinessDate >= from && s.BusinessDate <= to);
        var totalBills = await inPeriod.CountAsync(ct);
        var perCustomer = await inPeriod.Where(s => s.CustomerId != null).GroupBy(s => s.CustomerId!.Value)
            .Select(g => new { Id = g.Key, Bills = g.Count(), Spend = g.Sum(x => x.NetAmount) }).ToListAsync(ct);
        var ids = perCustomer.Select(x => x.Id).ToList();
        var returningIds = await posted.Where(s => s.CustomerId != null && ids.Contains(s.CustomerId!.Value) && s.BusinessDate < from)
            .Select(s => s.CustomerId!.Value).Distinct().ToListAsync(ct);
        var identified = perCustomer.Sum(x => x.Bills);
        var repeat = perCustomer.Count(x => x.Bills >= 2);
        var top = perCustomer.OrderByDescending(x => x.Spend).Take(10).ToList();
        var names = await db.Customers.AsNoTracking().Where(c => top.Select(t => t.Id).Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return new RetentionReport(from, to, totalBills, identified, totalBills == 0 ? 0 : Money.R2(identified * 100m / totalBills), perCustomer.Count,
            perCustomer.Count - returningIds.Count, returningIds.Count, repeat, perCustomer.Count == 0 ? 0 : Money.R2(repeat * 100m / perCustomer.Count),
            top.Select(t => new TopCustomer(t.Id, names[t.Id].Name, names[t.Id].Phone, t.Bills, t.Spend)).ToList());
    }
}

/// <summary>Points ledger. Earn/redeem happen inside the sale transaction; reversals on cancel/return.</summary>
public class LoyaltyService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit)
{
    public async Task<LoyaltySettings> SettingsAsync(CancellationToken ct = default)
    {
        var t = await db.Tenants.AsNoTracking().SingleAsync(x => x.Id == ctx.TenantId, ct);
        return new LoyaltySettings(t.LoyaltyEnabled, t.LoyaltyRupeesPerPoint, t.LoyaltyPointValue, t.LoyaltyMinRedeemPoints);
    }

    public async Task<LoyaltySettings> UpdateSettingsAsync(LoyaltySettings s, CancellationToken ct = default)
    {
        Require(s.RupeesPerPoint is >= 1 and <= 100000, "Spend per point must be ≥ ₹1.");
        Require(s.PointValue is > 0 and <= 100, "Point value must be between ₹0.01 and ₹100.");
        Require(s.MinRedeemPoints >= 0, "Minimum redeem points cannot be negative.");
        var t = await db.Tenants.SingleAsync(x => x.Id == ctx.TenantId, ct);
        t.LoyaltyEnabled = s.Enabled; t.LoyaltyRupeesPerPoint = s.RupeesPerPoint; t.LoyaltyPointValue = s.PointValue; t.LoyaltyMinRedeemPoints = s.MinRedeemPoints;
        audit.Record("loyalty.settings", nameof(Tenant), t.Id, $"enabled {s.Enabled}, ₹{s.RupeesPerPoint}/pt, value ₹{s.PointValue}");
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<int> BalanceAsync(Guid customerId, CancellationToken ct = default) =>
        await db.LoyaltyEntries.Where(e => e.CustomerId == customerId).SumAsync(e => (int?)e.Points, ct) ?? 0;

    public async Task<LoyaltyAccount> AccountAsync(Guid customerId, CancellationToken ct = default)
    {
        var s = await SettingsAsync(ct);
        var entries = await db.LoyaltyEntries.AsNoTracking().Where(e => e.CustomerId == customerId).OrderByDescending(e => e.CreatedAt).Take(200)
            .GroupJoin(db.SalesInvoices, e => e.SalesInvoiceId, i => i.Id, (e, i) => new { e, i })
            .SelectMany(x => x.i.DefaultIfEmpty(), (x, i) => new LoyaltyEntryDto(x.e.Type, x.e.Points, i != null ? i.InvoiceNo : null, x.e.Note, x.e.CreatedAt))
            .ToListAsync(ct);
        var points = await BalanceAsync(customerId, ct);
        return new LoyaltyAccount(customerId, points, Money.R2(points * s.PointValue), s, entries);
    }

    public async Task<LoyaltyAccount> AdjustAsync(Guid customerId, AdjustPointsRequest r, CancellationToken ct = default)
    {
        Require(r.Points != 0, "Points must be non-zero.");
        Require(!string.IsNullOrWhiteSpace(r.Reason), "Reason is required.");
        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct)) throw BusinessException.NotFound("Customer");
        db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = customerId, Type = LoyaltyEntryType.Adjust, Points = r.Points, Note = r.Reason.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId });
        audit.Record("loyalty.adjusted", nameof(Customer), customerId, $"{r.Points} pts: {r.Reason}");
        await db.SaveChangesAsync(ct);
        return await AccountAsync(customerId, ct);
    }

    /// <summary>Validates a loyalty payment and returns the points it consumes.</summary>
    public async Task<int> ValidateRedemptionAsync(Guid? customerId, decimal amount, CancellationToken ct)
    {
        var s = await SettingsAsync(ct);
        if (!s.Enabled) throw BusinessException.Validation("Loyalty is not enabled.");
        if (customerId is null) throw BusinessException.Validation("Loyalty payment needs a customer.");
        var pointsDecimal = amount / s.PointValue;
        if (pointsDecimal != decimal.Truncate(pointsDecimal)) throw BusinessException.Validation($"Loyalty amount must be a multiple of ₹{s.PointValue} (one point).");
        var points = (int)pointsDecimal;
        if (points < s.MinRedeemPoints) throw BusinessException.Validation($"Minimum {s.MinRedeemPoints} points to redeem.");
        var balance = await BalanceAsync(customerId.Value, ct);
        if (points > balance) throw BusinessException.Conflict("insufficient_points", $"Customer has only {balance} points.");
        return points;
    }

    /// <summary>Adds Redeem and Earn entries for a new sale (same unit of work as the invoice).</summary>
    public async Task RecordSaleAsync(SalesInvoice invoice, int redeemedPoints, CancellationToken ct)
    {
        if (invoice.CustomerId is null) return;
        var s = await SettingsAsync(ct);
        if (!s.Enabled && redeemedPoints == 0) return;
        var now = clock.UtcNow;
        if (redeemedPoints > 0)
            db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = invoice.CustomerId.Value, Type = LoyaltyEntryType.Redeem, Points = -redeemedPoints, SalesInvoiceId = invoice.Id, CreatedAt = now, CreatedByUserId = ctx.UserId });
        if (!s.Enabled) return;
        var earnBase = invoice.NetAmount - invoice.Payments.Where(p => p.Mode == PaymentMode.Loyalty).Sum(p => p.Amount);
        var earned = (int)Math.Floor(earnBase / s.RupeesPerPoint);
        if (earned > 0)
            db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = invoice.CustomerId.Value, Type = LoyaltyEntryType.Earn, Points = earned, SalesInvoiceId = invoice.Id, CreatedAt = now, CreatedByUserId = ctx.UserId });
    }

    /// <summary>Full cancel: take back earned points and give back redeemed points. Balance may go negative if already spent.</summary>
    public async Task ReverseSaleAsync(SalesInvoice invoice, CancellationToken ct)
    {
        if (invoice.CustomerId is null) return;
        var entries = await db.LoyaltyEntries.Where(e => e.SalesInvoiceId == invoice.Id).ToListAsync(ct);
        var earned = entries.Where(e => e.Type is LoyaltyEntryType.Earn or LoyaltyEntryType.ReverseEarn).Sum(e => e.Points);
        var redeemed = -entries.Where(e => e.Type is LoyaltyEntryType.Redeem or LoyaltyEntryType.ReverseRedeem).Sum(e => e.Points);
        var now = clock.UtcNow;
        if (earned > 0) db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = invoice.CustomerId.Value, Type = LoyaltyEntryType.ReverseEarn, Points = -earned, SalesInvoiceId = invoice.Id, Note = "Bill cancelled", CreatedAt = now, CreatedByUserId = ctx.UserId });
        if (redeemed > 0) db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = invoice.CustomerId.Value, Type = LoyaltyEntryType.ReverseRedeem, Points = redeemed, SalesInvoiceId = invoice.Id, Note = "Bill cancelled", CreatedAt = now, CreatedByUserId = ctx.UserId });
    }

    /// <summary>
    /// Return: take back earned points in proportion to the refund, rounded UP (so splitting a return into
    /// small pieces can't keep points), never more than what remains un-reversed for the bill.
    /// Redeemed points are not restored on a partial return; the refund is paid in the chosen mode.
    /// </summary>
    public async Task ReverseForReturnAsync(SalesInvoice invoice, SalesReturn ret, CancellationToken ct)
    {
        if (invoice.CustomerId is null) return;
        var entries = await db.LoyaltyEntries.Where(e => e.SalesInvoiceId == invoice.Id).ToListAsync(ct);
        var originallyEarned = entries.Where(e => e.Type == LoyaltyEntryType.Earn).Sum(e => e.Points);
        var remaining = originallyEarned + entries.Where(e => e.Type == LoyaltyEntryType.ReverseEarn).Sum(e => e.Points);
        if (originallyEarned <= 0 || remaining <= 0) return;
        var loyaltyPaid = await db.SalesPayments.Where(p => p.SalesInvoiceId == invoice.Id && p.Mode == PaymentMode.Loyalty).SumAsync(p => (decimal?)p.Amount, ct) ?? 0;
        var earnBase = invoice.NetAmount - loyaltyPaid;
        if (earnBase <= 0) return;
        var take = Math.Min(remaining, (int)Math.Ceiling(originallyEarned * ret.RefundAmount / earnBase - 0.0001m));
        if (take > 0)
            db.LoyaltyEntries.Add(new LoyaltyEntry { CustomerId = invoice.CustomerId.Value, Type = LoyaltyEntryType.ReverseEarn, Points = -take, SalesInvoiceId = invoice.Id, SalesReturnId = ret.Id, Note = ret.ReturnNo, CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId });
    }
}
