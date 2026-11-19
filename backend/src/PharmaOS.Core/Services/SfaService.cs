using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------
public record TerritoryInput(string Name, Guid? RepUserId, Guid? ManagerUserId, bool IsActive);
public record TerritoryDto(Guid Id, string Name, Guid? RepUserId, string? RepName, Guid? ManagerUserId, string? ManagerName, bool IsActive, int Doctors);
public record DoctorInput(string Name, string? Speciality, string? Qualification, string? Clinic, string? Area, string? Phone, Guid TerritoryId,
    DoctorClass Class, DateOnly? ConsentRecordedOn, bool IsActive, int? Version);
public record DoctorDto(Guid Id, string Name, string? Speciality, string? Qualification, string? Clinic, string? Area, string? Phone, Guid TerritoryId,
    string TerritoryName, DoctorClass Class, DateOnly? ConsentRecordedOn, bool IsActive, bool Anonymised, int Version, DateOnly? LastVisit);
public record TourEntryInput(DateOnly Date, Guid DoctorId);
public record TourPlanInput(List<TourEntryInput> Entries, int Version);
public record TourEntryDto(DateOnly Date, Guid DoctorId, string DoctorName, DoctorClass Class);
public record TourPlanDto(Guid Id, Guid RepUserId, string RepName, DateOnly Month, SfaStatus Status, string? ReviewComment, string? ReviewedBy,
    int Version, List<TourEntryDto> Entries);
public record ReviewInput(string? Comment);
public record SampleGiveInput(Guid ProductId, decimal Qty);
public record CallInput(Guid ClientRequestId, Guid DoctorId, string? ProductsDetailed, string? Feedback, Guid? JointWorkUserId,
    decimal? Latitude, decimal? Longitude, List<SampleGiveInput>? Samples);
public record SampleLineDto(Guid ProductId, string ProductName, decimal Qty);
public record CallDto(Guid Id, Guid DoctorId, string DoctorName, DoctorClass Class, DateTimeOffset CallAt, string? ProductsDetailed, string? Feedback,
    string? JointWorkWith, bool HasLocation, bool WasPlanned, List<SampleLineDto> Samples);
public record DailyReportDto(Guid Id, Guid RepUserId, string RepName, DateOnly Date, SfaStatus Status, string? Notes, string? ReviewComment,
    string? ReviewedBy, int Version, List<CallDto> Calls, List<TourEntryDto> Planned);
public record OpenDayRequest(DateOnly Date);
public record SubmitReportInput(string? Notes, int Version);
public record ReportListItem(Guid Id, Guid RepUserId, string RepName, DateOnly Date, SfaStatus Status, int Calls, DateTimeOffset? SubmittedAt);
public record SampleIssueInput(Guid RepUserId, Guid ProductId, decimal Qty, string? Note);
public record SampleBalanceRow(Guid RepUserId, string RepName, Guid ProductId, string ProductName, decimal Issued, decimal Given, decimal Returned, decimal Balance);
public record SampleLedgerRow(DateTimeOffset At, string RepName, string ProductName, SampleMoveKind Kind, decimal Qty, string? DoctorName, string? Note);
public record MissedDoctor(Guid DoctorId, string Name, DoctorClass Class, int Required, int Visits);
public record CoverageRow(Guid RepUserId, string RepName, int Doctors, int RequiredVisits, int Visits, int DoctorsMet, decimal CoveragePercent,
    int Planned, int PlannedMet, int ReportsPending, List<MissedDoctor> Missed);
public record TeamMember(Guid UserId, string Name, UserRole Role, List<string> Territories);

/// <summary>
/// Field force (MR) CRM. Scope: an MR sees only their own territories, doctors, plans, reports and sample bag; an area
/// manager sees the territories they supervise and the MRs working them; the owner sees everything.
/// Visit norms (A 4, B 2, C 1 per month) and the 7-day reporting window are defaults to confirm with the customer.
/// Doctor records are personal data — the consent date and anonymisation exist for that; legal basis needs review.
/// </summary>
public partial class SfaService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit)
{
    public const int ReportWindowDays = 7;
    public static int VisitNorm(DoctorClass c) => c switch { DoctorClass.A => 4, DoctorClass.B => 2, _ => 1 };

    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);
    private bool IsRep => ctx.Role == UserRole.MedicalRep;
    private bool IsManager => ctx.Role == UserRole.AreaManager;
    private bool IsOwner => ctx.Role == UserRole.Owner;

    private IQueryable<Territory> VisibleTerritories() =>
        IsRep ? db.Territories.Where(t => t.RepUserId == ctx.UserId)
        : IsManager ? db.Territories.Where(t => t.ManagerUserId == ctx.UserId)
        : db.Territories;

    private IQueryable<Doctor> VisibleDoctors()
    {
        var ids = VisibleTerritories().Select(t => t.Id);
        return db.Doctors.Where(d => ids.Contains(d.TerritoryId));
    }

    /// <summary>MRs whose plans/reports/samples the caller may see.</summary>
    private async Task<List<Guid>> VisibleRepsAsync(CancellationToken ct)
    {
        if (IsRep) return [ctx.UserId];
        if (IsManager) return await db.Territories.Where(t => t.ManagerUserId == ctx.UserId && t.RepUserId != null).Select(t => t.RepUserId!.Value).Distinct().ToListAsync(ct);
        return await db.Users.Where(u => u.Role == UserRole.MedicalRep).Select(u => u.Id).ToListAsync(ct);
    }

    private async Task EnsureRepVisibleAsync(Guid repUserId, CancellationToken ct)
    {
        if (!(await VisibleRepsAsync(ct)).Contains(repUserId)) throw BusinessException.NotFound("Representative");
    }

    /// <summary>Only the owner or a manager of one of the MR's territories may approve their plans/reports or issue samples.</summary>
    private async Task EnsureCanSuperviseAsync(Guid repUserId, CancellationToken ct)
    {
        if (IsOwner) return;
        if (IsManager && await db.Territories.AnyAsync(t => t.RepUserId == repUserId && t.ManagerUserId == ctx.UserId, ct)) return;
        throw BusinessException.Forbidden("Only this representative's area manager or the owner can do this.");
    }

    private Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return db.Users.Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
    }

    // ===================== team & territories =====================

    public async Task<List<TeamMember>> TeamAsync(CancellationToken ct = default)
    {
        var terr = await VisibleTerritories().AsNoTracking().ToListAsync(ct);
        var reps = await VisibleRepsAsync(ct);
        var managerIds = terr.Where(t => t.ManagerUserId != null).Select(t => t.ManagerUserId!.Value);
        var ids = reps.Concat(managerIds).Distinct().ToList();
        if (IsOwner) ids.AddRange(await db.Users.Where(u => u.Role == UserRole.AreaManager && u.IsActive).Select(u => u.Id).ToListAsync(ct));
        var users = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id) && u.IsActive).ToListAsync(ct);
        return users.OrderBy(u => u.Role).ThenBy(u => u.DisplayName).Select(u => new TeamMember(u.Id, u.DisplayName, u.Role,
            terr.Where(t => t.RepUserId == u.Id || t.ManagerUserId == u.Id).Select(t => t.Name).OrderBy(n => n).ToList())).ToList();
    }

    public async Task<List<TerritoryDto>> TerritoriesAsync(CancellationToken ct = default)
    {
        var list = await VisibleTerritories().AsNoTracking().OrderBy(t => t.Name).ToListAsync(ct);
        var ids = list.Select(t => t.Id).ToList();
        var counts = await db.Doctors.Where(d => ids.Contains(d.TerritoryId) && d.IsActive).GroupBy(d => d.TerritoryId)
            .Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);
        var names = await NamesAsync(list.SelectMany(t => new[] { t.RepUserId, t.ManagerUserId }).Where(x => x != null).Select(x => x!.Value), ct);
        return list.Select(t => new TerritoryDto(t.Id, t.Name, t.RepUserId, t.RepUserId is { } r ? names.GetValueOrDefault(r) : null,
            t.ManagerUserId, t.ManagerUserId is { } m ? names.GetValueOrDefault(m) : null, t.IsActive, counts.GetValueOrDefault(t.Id))).ToList();
    }

    public async Task<TerritoryDto> SaveTerritoryAsync(Guid? id, TerritoryInput i, CancellationToken ct = default)
    {
        Require(IsOwner, "Only the owner sets up territories.");
        Require(!string.IsNullOrWhiteSpace(i.Name), "Territory name is required.");
        if (i.RepUserId is { } rep && !await db.Users.AnyAsync(u => u.Id == rep && u.Role == UserRole.MedicalRep && u.IsActive, ct))
            throw BusinessException.Validation("Choose an active medical representative.");
        if (i.ManagerUserId is { } mgr && !await db.Users.AnyAsync(u => u.Id == mgr && u.Role == UserRole.AreaManager && u.IsActive, ct))
            throw BusinessException.Validation("Choose an active area manager.");
        var name = i.Name.Trim();
        if (await db.Territories.AnyAsync(t => t.Name == name && t.Id != id, ct)) throw BusinessException.Conflict("duplicate", "A territory with this name exists.");
        Territory t;
        if (id is null) { t = new Territory(); db.Territories.Add(t); }
        else t = await db.Territories.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Territory");
        t.Name = name; t.RepUserId = i.RepUserId; t.ManagerUserId = i.ManagerUserId; t.IsActive = i.IsActive;
        audit.Record(id is null ? "territory.created" : "territory.updated", nameof(Territory), t.Id, name);
        await db.SaveChangesAsync(ct);
        return (await TerritoriesAsync(ct)).Single(x => x.Id == t.Id);
    }

    // ===================== doctors =====================

    public async Task<List<DoctorDto>> DoctorsAsync(string? q, Guid? territoryId, bool includeInactive, CancellationToken ct = default)
    {
        var query = VisibleDoctors().AsNoTracking();
        if (!includeInactive) query = query.Where(d => d.IsActive);
        if (territoryId is { } tid) query = query.Where(d => d.TerritoryId == tid);
        if (!string.IsNullOrWhiteSpace(q)) { var s = q.Trim(); query = query.Where(d => d.Name.Contains(s) || d.Clinic!.Contains(s) || d.Area!.Contains(s) || d.Speciality!.Contains(s)); }
        var list = await query.Include(d => d.Territory).OrderBy(d => d.Name).Take(500).ToListAsync(ct);
        return await ToDtosAsync(list, ct);
    }

    private async Task<List<DoctorDto>> ToDtosAsync(List<Doctor> list, CancellationToken ct)
    {
        var ids = list.Select(d => d.Id).ToList();
        var last = await db.DoctorCalls.Where(c => ids.Contains(c.DoctorId))
            .Join(db.DailyReports, c => c.DailyReportId, r => r.Id, (c, r) => new { c.DoctorId, r.Date })
            .GroupBy(x => x.DoctorId).Select(g => new { g.Key, Last = g.Max(x => x.Date) }).ToDictionaryAsync(x => x.Key, x => x.Last, ct);
        return list.Select(d => new DoctorDto(d.Id, d.Name, d.Speciality, d.Qualification, d.Clinic, d.Area, d.Phone, d.TerritoryId, d.Territory?.Name ?? "",
            d.Class, d.ConsentRecordedOn, d.IsActive, d.AnonymisedAt != null, d.Version, last.TryGetValue(d.Id, out var l) ? l : null)).ToList();
    }

    public async Task<DoctorDto> SaveDoctorAsync(Guid? id, DoctorInput i, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name), "Doctor name is required.");
        Require(Enum.IsDefined(i.Class), "Class must be A, B or C.");
        Require(i.ConsentRecordedOn is null || i.ConsentRecordedOn <= Today, "Consent date cannot be in the future.");
        if (!await VisibleTerritories().AnyAsync(t => t.Id == i.TerritoryId && t.IsActive, ct))
            throw BusinessException.Validation("Choose one of your active territories.");
        Doctor d;
        if (id is null) { d = new Doctor(); db.Doctors.Add(d); }
        else
        {
            d = await VisibleDoctors().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Doctor");
            if (d.AnonymisedAt is not null) throw BusinessException.Conflict("anonymised", "This doctor's details were removed and cannot be edited.");
            Require(i.Version is not null, "Version is required.");
            db.Entry(d).Property(x => x.Version).OriginalValue = i.Version!.Value;
        }
        static string? T(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        d.Name = i.Name.Trim(); d.Speciality = T(i.Speciality); d.Qualification = T(i.Qualification); d.Clinic = T(i.Clinic); d.Area = T(i.Area);
        d.Phone = T(i.Phone); d.TerritoryId = i.TerritoryId; d.Class = i.Class; d.ConsentRecordedOn = i.ConsentRecordedOn; d.IsActive = i.IsActive;
        audit.Record(id is null ? "doctor.created" : "doctor.updated", nameof(Doctor), d.Id, $"class {d.Class}");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "Changed by someone else. Reload."); }
        await db.Entry(d).Reference(x => x.Territory).LoadAsync(ct);
        return (await ToDtosAsync([d], ct)).Single();
    }

    /// <summary>Removes the doctor's personal details on request; visit counts stay for reporting.</summary>
    public async Task AnonymiseDoctorAsync(Guid id, CancellationToken ct = default)
    {
        Require(IsOwner, "Only the owner can remove a doctor's details.");
        var d = await db.Doctors.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Doctor");
        if (d.AnonymisedAt is not null) return;
        d.Name = "Removed doctor"; d.Speciality = d.Qualification = d.Clinic = d.Area = d.Phone = null; d.ConsentRecordedOn = null;
        d.IsActive = false; d.AnonymisedAt = clock.UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.DoctorCalls.Where(c => c.DoctorId == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Feedback, (string?)null).SetProperty(c => c.Latitude, (decimal?)null).SetProperty(c => c.Longitude, (decimal?)null), ct);
        audit.Record("doctor.anonymised", nameof(Doctor), id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    // ===================== tour plans =====================

    private static DateOnly MonthStart(DateOnly d) => new(d.Year, d.Month, 1);

    public async Task<TourPlanDto> MyTourPlanAsync(DateOnly month, CancellationToken ct = default)
    {
        Require(IsRep, "Only medical representatives have tour plans.");
        var m = MonthStart(month);
        var p = await db.TourPlans.Include(x => x.Entries).SingleOrDefaultAsync(x => x.RepUserId == ctx.UserId && x.Month == m, ct);
        if (p is null)
        {
            Require(m >= MonthStart(Today), "Tour plans can only be started for this month or later.");
            p = new TourPlan { RepUserId = ctx.UserId, Month = m };
            db.TourPlans.Add(p);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                p = await db.TourPlans.Include(x => x.Entries).SingleAsync(x => x.RepUserId == ctx.UserId && x.Month == m, ct);
            }
        }
        return await ToDtoAsync(p, ct);
    }

    public async Task<List<TourPlanDto>> TourPlansAsync(DateOnly month, CancellationToken ct = default)
    {
        var reps = await VisibleRepsAsync(ct);
        var m = MonthStart(month);
        var list = await db.TourPlans.AsNoTracking().Include(x => x.Entries).Where(p => p.Month == m && reps.Contains(p.RepUserId)).ToListAsync(ct);
        var dtos = new List<TourPlanDto>();
        foreach (var p in list) dtos.Add(await ToDtoAsync(p, ct));
        return dtos.OrderBy(d => d.RepName).ToList();
    }

    public async Task<TourPlanDto> GetTourPlanAsync(Guid id, CancellationToken ct = default) => await ToDtoAsync(await LoadPlanAsync(id, ct), ct);

    private async Task<TourPlan> LoadPlanAsync(Guid id, CancellationToken ct)
    {
        var p = await db.TourPlans.Include(x => x.Entries).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Tour plan");
        await EnsureRepVisibleAsync(p.RepUserId, ct);
        return p;
    }

    public async Task<TourPlanDto> SaveTourPlanAsync(Guid id, TourPlanInput i, CancellationToken ct = default)
    {
        var p = await LoadPlanAsync(id, ct);
        Require(p.RepUserId == ctx.UserId, "Only the representative can change their own tour plan.");
        if (p.Status is not (SfaStatus.Draft or SfaStatus.Rejected)) throw BusinessException.Conflict("locked", "This plan has been submitted; ask your manager to send it back to change it.");
        var end = p.Month.AddMonths(1);
        Require(i.Entries.All(e => e.Date >= p.Month && e.Date < end), "Every visit must be in the plan's month.");
        Require(i.Entries.Count <= 1000, "Too many planned visits.");
        var doctorIds = i.Entries.Select(e => e.DoctorId).Distinct().ToList();
        var ok = await VisibleDoctors().Where(d => doctorIds.Contains(d.Id) && d.IsActive).Select(d => d.Id).ToListAsync(ct);
        Require(ok.Count == doctorIds.Count, "Plan only active doctors in your own territories.");
        db.Entry(p).Property(x => x.Version).OriginalValue = i.Version;
        db.TourPlanEntries.RemoveRange(p.Entries);
        p.Entries = i.Entries.DistinctBy(e => (e.Date, e.DoctorId)).Select(e => new TourPlanEntry { TourPlanId = p.Id, Date = e.Date, DoctorId = e.DoctorId }).ToList();
        db.TourPlanEntries.AddRange(p.Entries);
        p.Status = SfaStatus.Draft;
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "The plan changed in another window. Reload."); }
        return await ToDtoAsync(p, ct);
    }

    public async Task<TourPlanDto> SubmitTourPlanAsync(Guid id, CancellationToken ct = default)
    {
        var p = await LoadPlanAsync(id, ct);
        Require(p.RepUserId == ctx.UserId, "Only the representative can submit their tour plan.");
        if (p.Status is not (SfaStatus.Draft or SfaStatus.Rejected)) throw BusinessException.Conflict("invalid_state", "Already submitted.");
        Require(p.Entries.Count > 0, "Add at least one planned visit.");
        p.Status = SfaStatus.Submitted; p.SubmittedAt = clock.UtcNow;
        audit.Record("tour_plan.submitted", nameof(TourPlan), p.Id, $"{p.Month:yyyy-MM}, {p.Entries.Count} visits");
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(p, ct);
    }

    public async Task<TourPlanDto> ReviewTourPlanAsync(Guid id, bool approve, string? comment, CancellationToken ct = default)
    {
        var p = await LoadPlanAsync(id, ct);
        await EnsureCanSuperviseAsync(p.RepUserId, ct);
        if (p.Status != SfaStatus.Submitted) throw BusinessException.Conflict("invalid_state", "Only submitted plans can be reviewed.");
        Require(approve || !string.IsNullOrWhiteSpace(comment), "Say what to change when sending a plan back.");
        p.Status = approve ? SfaStatus.Approved : SfaStatus.Rejected;
        p.ReviewedByUserId = ctx.UserId; p.ReviewedAt = clock.UtcNow; p.ReviewComment = comment?.Trim();
        audit.Record(approve ? "tour_plan.approved" : "tour_plan.rejected", nameof(TourPlan), p.Id, comment);
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(p, ct);
    }

    private async Task<TourPlanDto> ToDtoAsync(TourPlan p, CancellationToken ct)
    {
        var docIds = p.Entries.Select(e => e.DoctorId).Distinct().ToList();
        var docs = await db.Doctors.Where(d => docIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, d => (d.Name, d.Class), ct);
        var names = await NamesAsync(p.ReviewedByUserId is { } r ? [p.RepUserId, r] : [p.RepUserId], ct);
        return new TourPlanDto(p.Id, p.RepUserId, names.GetValueOrDefault(p.RepUserId, ""), p.Month, p.Status, p.ReviewComment,
            p.ReviewedByUserId is { } rv ? names.GetValueOrDefault(rv) : null, p.Version,
            p.Entries.OrderBy(e => e.Date).ThenBy(e => docs.GetValueOrDefault(e.DoctorId).Name)
                .Select(e => new TourEntryDto(e.Date, e.DoctorId, docs.GetValueOrDefault(e.DoctorId).Name ?? "", docs.GetValueOrDefault(e.DoctorId).Class)).ToList());
    }

    // ===================== daily call reports =====================

    /// <summary>Opens (or creates) the caller's report for a day inside the reporting window.</summary>
    public async Task<DailyReportDto> OpenDayAsync(DateOnly date, CancellationToken ct = default)
    {
        Require(IsRep, "Only medical representatives file daily call reports.");
        var r = await db.DailyReports.SingleOrDefaultAsync(x => x.RepUserId == ctx.UserId && x.Date == date, ct);
        if (r is null)
        {
            if (date > Today) throw BusinessException.Conflict("future_date", "You cannot report calls for a future day.");
            if (date < Today.AddDays(-ReportWindowDays))
                throw BusinessException.Conflict("outside_window", $"Calls can be reported up to {ReportWindowDays} days late. Ask your manager about older days.");
            r = new DailyReport { RepUserId = ctx.UserId, Date = date };
            db.DailyReports.Add(r);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException)
            {
                db.ChangeTracker.Clear();
                r = await db.DailyReports.SingleAsync(x => x.RepUserId == ctx.UserId && x.Date == date, ct);
            }
        }
        return await ReportDtoAsync(r.Id, ct);
    }

    private async Task<DailyReport> LoadReportAsync(Guid id, CancellationToken ct)
    {
        var r = await db.DailyReports.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Daily report");
        await EnsureRepVisibleAsync(r.RepUserId, ct);
        return r;
    }

    private DailyReport EditableByMe(DailyReport r)
    {
        Require(r.RepUserId == ctx.UserId, "Only the representative can change their own report.");
        if (r.Status is not (SfaStatus.Draft or SfaStatus.Rejected))
            throw BusinessException.Conflict("locked", "This report has been submitted; ask your manager to send it back to change it.");
        return r;
    }

    public async Task<DailyReportDto> GetReportAsync(Guid id, CancellationToken ct = default) => await ReportDtoAsync((await LoadReportAsync(id, ct)).Id, ct);

    public async Task<DailyReportDto> AddCallAsync(Guid reportId, CallInput i, CancellationToken ct = default)
    {
        Require(i.ClientRequestId != Guid.Empty, "clientRequestId is required.");
        var loaded = await LoadReportAsync(reportId, ct);
        var existing = await db.DoctorCalls.AsNoTracking().FirstOrDefaultAsync(c => c.ClientRequestId == i.ClientRequestId, ct);
        if (existing is not null)
        {
            Require(existing.DailyReportId == reportId, "This request id was already used for another report.");
            return await ReportDtoAsync(reportId, ct); // retried save
        }
        var r = EditableByMe(loaded);
        var doctor = await VisibleDoctors().SingleOrDefaultAsync(d => d.Id == i.DoctorId, ct) ?? throw BusinessException.NotFound("Doctor");
        Require(doctor.IsActive, "This doctor is inactive.");
        if (await db.DoctorCalls.AnyAsync(c => c.DailyReportId == r.Id && c.DoctorId == i.DoctorId, ct))
            throw BusinessException.Conflict("duplicate_call", $"{doctor.Name} is already in this day's report.");
        if (i.JointWorkUserId is { } jw && !await db.Users.AnyAsync(u => u.Id == jw && (u.Role == UserRole.AreaManager || u.Role == UserRole.Owner) && u.IsActive, ct))
            throw BusinessException.Validation("Joint work can only be recorded with a manager.");
        Require((i.Latitude is null) == (i.Longitude is null), "Send both latitude and longitude, or neither.");
        Require(i.Latitude is null || (i.Latitude is >= -90 and <= 90 && i.Longitude is >= -180 and <= 180), "Location is out of range.");
        var samples = (i.Samples ?? []).Where(s => s.Qty != 0).ToList();
        Require(samples.All(s => s.Qty > 0 && s.Qty == Math.Floor(s.Qty) && s.Qty <= 1000), "Sample quantities must be whole packs.");
        Require(samples.Select(s => s.ProductId).Distinct().Count() == samples.Count, "List each sample product once.");

        var planned = await db.TourPlans.Where(p => p.RepUserId == r.RepUserId && p.Month == MonthStart(r.Date) && p.Status == SfaStatus.Approved)
            .SelectMany(p => p.Entries).AnyAsync(e => e.Date == r.Date && e.DoctorId == i.DoctorId, ct);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (samples.Count > 0) await LockSampleBagAsync(r.RepUserId, ct);
        var call = new DoctorCall
        {
            DailyReportId = r.Id, DoctorId = doctor.Id, CallAt = clock.UtcNow, ProductsDetailed = i.ProductsDetailed?.Trim(), Feedback = i.Feedback?.Trim(),
            JointWorkUserId = i.JointWorkUserId, Latitude = i.Latitude, Longitude = i.Longitude, WasPlanned = planned, ClientRequestId = i.ClientRequestId,
        };
        db.DoctorCalls.Add(call);
        if (samples.Count > 0)
        {
            var bag = await BalancesForAsync(r.RepUserId, ct);
            foreach (var s in samples)
            {
                var have = bag.GetValueOrDefault(s.ProductId);
                if (have < s.Qty)
                {
                    var name = await db.Products.Where(p => p.Id == s.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? "this product";
                    throw BusinessException.Conflict("insufficient_samples", $"Only {have:0} pack(s) of {name} in your sample bag.");
                }
                db.SampleMovements.Add(new SampleMovement
                {
                    RepUserId = r.RepUserId, ProductId = s.ProductId, Kind = SampleMoveKind.GivenToDoctor, Qty = -s.Qty, DoctorId = doctor.Id,
                    DoctorCallId = call.Id, CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
                });
            }
        }
        db.Entry(r).Property(x => x.Notes).IsModified = true; // bumps Version: a concurrent submit cannot slip past this call
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "The report changed in another window. Reload."); }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            // the same save raced itself (duplicate clientRequestId): return the stored result, otherwise surface the error
            if (!await db.DoctorCalls.AsNoTracking().AnyAsync(c => c.ClientRequestId == i.ClientRequestId, CancellationToken.None)) throw;
        }
        return await ReportDtoAsync(r.Id, ct);
    }

    public async Task<DailyReportDto> RemoveCallAsync(Guid reportId, Guid callId, CancellationToken ct = default)
    {
        var r = EditableByMe(await LoadReportAsync(reportId, ct));
        var call = await db.DoctorCalls.SingleOrDefaultAsync(c => c.Id == callId && c.DailyReportId == r.Id, ct) ?? throw BusinessException.NotFound("Call");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var given = await db.SampleMovements.Where(m => m.DoctorCallId == call.Id).GroupBy(m => m.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.Qty) }).ToListAsync(ct);
        foreach (var g in given.Where(g => g.Qty != 0))
            db.SampleMovements.Add(new SampleMovement
            {
                RepUserId = r.RepUserId, ProductId = g.Key, Kind = SampleMoveKind.Reversal, Qty = -g.Qty, DoctorId = call.DoctorId, DoctorCallId = call.Id,
                Note = "Call removed from report", CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
            });
        db.DoctorCalls.Remove(call);
        db.Entry(r).Property(x => x.Notes).IsModified = true;
        try { await db.SaveChangesAsync(ct); await tx.CommitAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "The report changed in another window. Reload."); }
        return await ReportDtoAsync(r.Id, ct);
    }

    public async Task<DailyReportDto> SubmitReportAsync(Guid id, SubmitReportInput i, CancellationToken ct = default)
    {
        var r = EditableByMe(await LoadReportAsync(id, ct));
        var calls = await db.DoctorCalls.CountAsync(c => c.DailyReportId == r.Id, ct);
        Require(calls > 0 || !string.IsNullOrWhiteSpace(i.Notes), "Add calls, or a note explaining the day (meeting, leave, travel).");
        db.Entry(r).Property(x => x.Version).OriginalValue = i.Version;
        r.Notes = i.Notes?.Trim(); r.Status = SfaStatus.Submitted; r.SubmittedAt = clock.UtcNow;
        audit.Record("dcr.submitted", nameof(DailyReport), r.Id, $"{r.Date:yyyy-MM-dd}, {calls} call(s)");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw BusinessException.Conflict("concurrency", "The report changed in another window. Reload."); }
        return await ReportDtoAsync(r.Id, ct);
    }

    public async Task<DailyReportDto> ReviewReportAsync(Guid id, bool approve, string? comment, CancellationToken ct = default)
    {
        var r = await LoadReportAsync(id, ct);
        await EnsureCanSuperviseAsync(r.RepUserId, ct);
        if (r.Status != SfaStatus.Submitted) throw BusinessException.Conflict("invalid_state", "Only submitted reports can be reviewed.");
        Require(approve || !string.IsNullOrWhiteSpace(comment), "Say what to correct when sending a report back.");
        r.Status = approve ? SfaStatus.Approved : SfaStatus.Rejected;
        r.ReviewedByUserId = ctx.UserId; r.ReviewedAt = clock.UtcNow; r.ReviewComment = comment?.Trim();
        audit.Record(approve ? "dcr.approved" : "dcr.rejected", nameof(DailyReport), r.Id, comment);
        await db.SaveChangesAsync(ct);
        return await ReportDtoAsync(r.Id, ct);
    }

    public async Task<List<ReportListItem>> ReportsAsync(DateOnly from, DateOnly to, Guid? repUserId, SfaStatus? status, CancellationToken ct = default)
    {
        Require(to >= from && to.DayNumber - from.DayNumber <= 92, "Choose a range of up to 3 months.");
        var reps = await VisibleRepsAsync(ct);
        if (repUserId is { } rid) reps = reps.Where(x => x == rid).ToList();
        var q = db.DailyReports.AsNoTracking().Where(r => reps.Contains(r.RepUserId) && r.Date >= from && r.Date <= to);
        if (status is { } s) q = q.Where(r => r.Status == s);
        var rows = await q.OrderByDescending(r => r.Date).Select(r => new { r.Id, r.RepUserId, r.Date, r.Status, r.SubmittedAt, Calls = db.DoctorCalls.Count(c => c.DailyReportId == r.Id) })
            .Take(1000).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.RepUserId), ct);
        return rows.Select(r => new ReportListItem(r.Id, r.RepUserId, names.GetValueOrDefault(r.RepUserId, ""), r.Date, r.Status, r.Calls, r.SubmittedAt)).ToList();
    }

    private async Task<DailyReportDto> ReportDtoAsync(Guid id, CancellationToken ct)
    {
        var r = await db.DailyReports.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        var calls = await db.DoctorCalls.AsNoTracking().Include(c => c.Doctor).Where(c => c.DailyReportId == id).OrderBy(c => c.CallAt).ToListAsync(ct);
        var callIds = calls.Select(c => c.Id).ToList();
        var samples = await db.SampleMovements.AsNoTracking().Where(m => m.DoctorCallId != null && callIds.Contains(m.DoctorCallId.Value))
            .GroupBy(m => new { m.DoctorCallId, m.ProductId, m.Product!.Name }).Select(g => new { g.Key.DoctorCallId, g.Key.ProductId, g.Key.Name, Qty = -g.Sum(m => m.Qty) })
            .ToListAsync(ct);
        var planned = await db.TourPlans.AsNoTracking().Where(p => p.RepUserId == r.RepUserId && p.Month == MonthStart(r.Date) && p.Status == SfaStatus.Approved)
            .SelectMany(p => p.Entries).Where(e => e.Date == r.Date)
            .Join(db.Doctors, e => e.DoctorId, d => d.Id, (e, d) => new TourEntryDto(e.Date, d.Id, d.Name, d.Class)).ToListAsync(ct);
        var names = await NamesAsync(calls.Where(c => c.JointWorkUserId != null).Select(c => c.JointWorkUserId!.Value)
            .Append(r.RepUserId).Concat(r.ReviewedByUserId is { } rv ? new[] { rv } : Array.Empty<Guid>()), ct);
        return new DailyReportDto(r.Id, r.RepUserId, names.GetValueOrDefault(r.RepUserId, ""), r.Date, r.Status, r.Notes, r.ReviewComment,
            r.ReviewedByUserId is { } x ? names.GetValueOrDefault(x) : null, r.Version,
            calls.Select(c => new CallDto(c.Id, c.DoctorId, c.Doctor?.Name ?? "", c.Doctor?.Class ?? DoctorClass.C, c.CallAt, c.ProductsDetailed, c.Feedback,
                c.JointWorkUserId is { } j ? names.GetValueOrDefault(j) : null, c.Latitude != null, c.WasPlanned,
                samples.Where(s => s.DoctorCallId == c.Id && s.Qty != 0).Select(s => new SampleLineDto(s.ProductId, s.Name, s.Qty)).ToList())).ToList(),
            planned.OrderBy(p => p.DoctorName).ToList());
    }

    // ===================== samples =====================

    private Task LockSampleBagAsync(Guid repUserId, CancellationToken ct) =>
        db.Database.ExecuteSqlRawAsync("EXEC sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
            [$"samples:{ctx.TenantId}:{repUserId}"], ct);

    private Task<Dictionary<Guid, decimal>> BalancesForAsync(Guid repUserId, CancellationToken ct) =>
        db.SampleMovements.Where(m => m.RepUserId == repUserId).GroupBy(m => m.ProductId)
            .Select(g => new { g.Key, Qty = g.Sum(m => m.Qty) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);

    public async Task IssueSamplesAsync(SampleIssueInput i, bool isReturn, CancellationToken ct = default)
    {
        Require(i.Qty > 0 && i.Qty == Math.Floor(i.Qty) && i.Qty <= 100000, "Quantity must be whole packs.");
        if (!await db.Users.AnyAsync(u => u.Id == i.RepUserId && u.Role == UserRole.MedicalRep, ct)) throw BusinessException.NotFound("Representative");
        await EnsureCanSuperviseAsync(i.RepUserId, ct);
        var product = await db.Products.Where(p => p.Id == i.ProductId).Select(p => p.Name).FirstOrDefaultAsync(ct) ?? throw BusinessException.NotFound("Product");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await LockSampleBagAsync(i.RepUserId, ct);
        if (isReturn)
        {
            var have = (await BalancesForAsync(i.RepUserId, ct)).GetValueOrDefault(i.ProductId);
            if (have < i.Qty) throw BusinessException.Conflict("insufficient_samples", $"The bag has only {have:0} pack(s) of {product}.");
        }
        db.SampleMovements.Add(new SampleMovement
        {
            RepUserId = i.RepUserId, ProductId = i.ProductId, Kind = isReturn ? SampleMoveKind.ReturnedByRep : SampleMoveKind.Issued,
            Qty = isReturn ? -i.Qty : i.Qty, Note = i.Note?.Trim(), CreatedByUserId = ctx.UserId, CreatedAt = clock.UtcNow,
        });
        audit.Record(isReturn ? "samples.returned" : "samples.issued", nameof(Product), i.ProductId, $"{i.Qty:0} pack(s) of {product}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<List<SampleBalanceRow>> SampleBalancesAsync(Guid? repUserId, CancellationToken ct = default)
    {
        var reps = await VisibleRepsAsync(ct);
        if (repUserId is { } rid) reps = reps.Where(x => x == rid).ToList();
        var rows = await db.SampleMovements.AsNoTracking().Where(m => reps.Contains(m.RepUserId))
            .GroupBy(m => new { m.RepUserId, m.ProductId, m.Product!.Name })
            .Select(g => new
            {
                g.Key.RepUserId, g.Key.ProductId, g.Key.Name,
                Issued = g.Where(m => m.Kind == SampleMoveKind.Issued).Sum(m => m.Qty),
                Given = -g.Where(m => m.Kind == SampleMoveKind.GivenToDoctor || m.Kind == SampleMoveKind.Reversal).Sum(m => m.Qty),
                Returned = -g.Where(m => m.Kind == SampleMoveKind.ReturnedByRep).Sum(m => m.Qty),
                Balance = g.Sum(m => m.Qty),
            }).ToListAsync(ct);
        var names = await NamesAsync(rows.Select(r => r.RepUserId), ct);
        return rows.Select(r => new SampleBalanceRow(r.RepUserId, names.GetValueOrDefault(r.RepUserId, ""), r.ProductId, r.Name, r.Issued, r.Given, r.Returned, r.Balance))
            .OrderBy(r => r.RepName).ThenBy(r => r.ProductName).ToList();
    }

    public async Task<List<SampleLedgerRow>> SampleLedgerAsync(Guid repUserId, CancellationToken ct = default)
    {
        await EnsureRepVisibleAsync(repUserId, ct);
        var rows = await db.SampleMovements.AsNoTracking().Where(m => m.RepUserId == repUserId).OrderByDescending(m => m.CreatedAt).Take(500)
            .Select(m => new { m.CreatedAt, m.Product!.Name, m.Kind, m.Qty, Doctor = db.Doctors.Where(d => d.Id == m.DoctorId).Select(d => d.Name).FirstOrDefault(), m.Note })
            .ToListAsync(ct);
        var rep = (await NamesAsync([repUserId], ct)).GetValueOrDefault(repUserId, "");
        return rows.Select(r => new SampleLedgerRow(r.CreatedAt, rep, r.Name, r.Kind, r.Qty, r.Doctor, r.Note)).ToList();
    }

    // ===================== coverage =====================

    /// <summary>Per MR for a month: doctors met against visit norms, tour-plan adherence, reports awaiting approval.</summary>
    public async Task<List<CoverageRow>> CoverageAsync(DateOnly month, CancellationToken ct = default)
    {
        var m = MonthStart(month); var end = m.AddMonths(1);
        var reps = await VisibleRepsAsync(ct);
        var names = await NamesAsync(reps, ct);
        var territories = await db.Territories.AsNoTracking().Where(t => t.RepUserId != null && reps.Contains(t.RepUserId!.Value) && t.IsActive)
            .Select(t => new { t.Id, Rep = t.RepUserId!.Value }).ToListAsync(ct);
        var terrIds = territories.Select(t => t.Id).ToList();
        var doctors = await db.Doctors.AsNoTracking().Where(d => terrIds.Contains(d.TerritoryId) && d.IsActive)
            .Select(d => new { d.Id, d.Name, d.Class, d.TerritoryId }).ToListAsync(ct);
        var calls = await db.DoctorCalls.AsNoTracking()
            .Join(db.DailyReports, c => c.DailyReportId, r => r.Id, (c, r) => new { c.DoctorId, r.RepUserId, r.Date, r.Status })
            .Where(x => reps.Contains(x.RepUserId) && x.Date >= m && x.Date < end && (x.Status == SfaStatus.Submitted || x.Status == SfaStatus.Approved))
            .ToListAsync(ct);
        var plans = await db.TourPlans.AsNoTracking().Where(p => reps.Contains(p.RepUserId) && p.Month == m && p.Status == SfaStatus.Approved)
            .SelectMany(p => p.Entries, (p, e) => new { p.RepUserId, e.Date, e.DoctorId }).ToListAsync(ct);
        var pending = await db.DailyReports.AsNoTracking().Where(r => reps.Contains(r.RepUserId) && r.Status == SfaStatus.Submitted)
            .GroupBy(r => r.RepUserId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N, ct);

        var result = new List<CoverageRow>();
        foreach (var rep in reps)
        {
            var mine = territories.Where(t => t.Rep == rep).Select(t => t.Id).ToHashSet();
            var docs = doctors.Where(d => mine.Contains(d.TerritoryId)).ToList();
            var myCalls = calls.Where(c => c.RepUserId == rep).ToList();
            var visits = myCalls.GroupBy(c => c.DoctorId).ToDictionary(g => g.Key, g => g.Count());
            var missed = docs.Where(d => visits.GetValueOrDefault(d.Id) < VisitNorm(d.Class))
                .Select(d => new MissedDoctor(d.Id, d.Name, d.Class, VisitNorm(d.Class), visits.GetValueOrDefault(d.Id)))
                .OrderBy(d => d.Class).ThenBy(d => d.Name).ToList();
            var myPlan = plans.Where(p => p.RepUserId == rep).ToList();
            var plannedMet = myPlan.Count(p => myCalls.Any(c => c.Date == p.Date && c.DoctorId == p.DoctorId));
            var met = docs.Count - missed.Count;
            result.Add(new CoverageRow(rep, names.GetValueOrDefault(rep, ""), docs.Count, docs.Sum(d => VisitNorm(d.Class)), myCalls.Count, met,
                docs.Count == 0 ? 0 : Math.Round(100m * met / docs.Count, 1), myPlan.Count, plannedMet, pending.GetValueOrDefault(rep), missed));
        }
        return result.OrderBy(r => r.RepName).ToList();
    }
}
