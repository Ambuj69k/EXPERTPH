using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

// Field force (medical representative) CRM / SFA. A pharma marketing company's MRs visit doctors, report calls daily,
// and hand out samples; area managers approve tour plans and daily reports.

/// <summary>A sales territory (headquarter/beat). One MR works it; one area manager supervises it.</summary>
public class Territory : TenantEntity
{
    public string Name { get; set; } = "";
    public Guid? RepUserId { get; set; }
    public Guid? ManagerUserId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>A = key doctor (visited most often), B = regular, C = occasional. Monthly visit norms per class are in SfaService.VisitNorm.</summary>
public enum DoctorClass { A = 1, B = 2, C = 3 }

/// <summary>
/// A doctor (or other prescriber) the field force calls on. This is personal data: keep only what the visit needs,
/// record the basis for holding it, and anonymise on request.
/// </summary>
public class Doctor : TenantEntity, IVersioned
{
    public string Name { get; set; } = "";
    public string? Speciality { get; set; }
    public string? Qualification { get; set; }
    public string? Clinic { get; set; }
    public string? Area { get; set; }
    public string? Phone { get; set; }
    public Guid TerritoryId { get; set; }
    public Territory? Territory { get; set; }
    public DoctorClass Class { get; set; } = DoctorClass.B;
    /// <summary>When the doctor agreed to be contacted / to their details being kept (null = not recorded).</summary>
    public DateOnly? ConsentRecordedOn { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset? AnonymisedAt { get; set; }
    public int Version { get; set; }
}

public enum SfaStatus { Draft = 1, Submitted = 2, Approved = 3, Rejected = 4 }

/// <summary>Monthly tour programme: which doctors the MR plans to see on which day. Approved by the area manager.</summary>
public class TourPlan : TenantEntity, IVersioned
{
    public Guid RepUserId { get; set; }
    /// <summary>First day of the month.</summary>
    public DateOnly Month { get; set; }
    public SfaStatus Status { get; set; } = SfaStatus.Draft;
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public int Version { get; set; }
    public List<TourPlanEntry> Entries { get; set; } = [];
}

public class TourPlanEntry : TenantEntity
{
    public Guid TourPlanId { get; set; }
    public DateOnly Date { get; set; }
    public Guid DoctorId { get; set; }
}

/// <summary>Daily call report (DCR): the MR's visits for one day. Locked once submitted; the manager approves or sends it back.</summary>
public class DailyReport : TenantEntity, IVersioned
{
    public Guid RepUserId { get; set; }
    public DateOnly Date { get; set; }
    public SfaStatus Status { get; set; } = SfaStatus.Draft;
    public string? Notes { get; set; }
    public DateTimeOffset? SubmittedAt { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public int Version { get; set; }
    public List<DoctorCall> Calls { get; set; } = [];
}

public class DoctorCall : TenantEntity
{
    public Guid DailyReportId { get; set; }
    public Guid DoctorId { get; set; }
    public Doctor? Doctor { get; set; }
    public DateTimeOffset CallAt { get; set; }
    /// <summary>Products discussed, as free text ("Brand A, Brand B").</summary>
    public string? ProductsDetailed { get; set; }
    public string? Feedback { get; set; }
    public Guid? JointWorkUserId { get; set; }
    /// <summary>Only stored when the MR chose to attach their location to this call.</summary>
    public decimal? Latitude { get; set; }
    public decimal? Longitude { get; set; }
    public bool WasPlanned { get; set; }
    /// <summary>Lets the mobile screen retry a save (e.g. after a network drop) without creating duplicates.</summary>
    public Guid ClientRequestId { get; set; }
}

public enum SampleMoveKind { Issued = 1, GivenToDoctor = 2, ReturnedByRep = 3, Reversal = 4 }

/// <summary>
/// Sample/promotional-input ledger per MR (immutable). Samples are tracked separately from saleable stock.
/// Qty is in packs: + into the MR's bag, − out of it.
/// </summary>
public class SampleMovement : TenantEntity, IImmutable
{
    public Guid RepUserId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public SampleMoveKind Kind { get; set; }
    public decimal Qty { get; set; }
    public Guid? DoctorId { get; set; }
    public Guid? DoctorCallId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum ExpenseCategory { Travel = 1, Food = 2, Lodging = 3, Other = 4 }

/// <summary>An MR's field expense for a day, approved by their area manager.</summary>
public class ExpenseClaim : TenantEntity
{
    public Guid RepUserId { get; set; }
    public DateOnly Date { get; set; }
    public ExpenseCategory Category { get; set; }
    public decimal Amount { get; set; }
    public decimal? Km { get; set; }
    public string? Note { get; set; }
    public SfaStatus Status { get; set; } = SfaStatus.Submitted;
    public Guid? ReviewedByUserId { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewComment { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Monthly target for an MR, set by the owner or their manager.</summary>
public class SfaTarget : TenantEntity
{
    public Guid RepUserId { get; set; }
    public DateOnly Month { get; set; }
    public int CallsTarget { get; set; }
    public decimal CoverageTargetPercent { get; set; }
}
