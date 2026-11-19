using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

// Hospital pharmacy: the central pharmacy store supplies wards/departments against indents. Patient billing, orders and
// medication administration stay in the hospital information system (HIS); this module only moves and costs stock.

/// <summary>A ward or department that consumes medicines (cost centre).</summary>
public class Department : TenantEntity
{
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>Hospital formulary: items wards may indent. High-alert items need a second pharmacist's check before issue.</summary>
public class FormularyItem : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public bool IsHighAlert { get; set; }
    public string? Notes { get; set; }
}

public enum IndentPriority { Routine = 1, Urgent = 2 }
public enum WardIndentStatus { Open = 1, PartlyIssued = 2, Closed = 3, Cancelled = 4 }

/// <summary>A ward's request to the pharmacy store.</summary>
public class WardIndent : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public string IndentNo { get; set; } = "";
    public WardIndentStatus Status { get; set; } = WardIndentStatus.Open;
    public IndentPriority Priority { get; set; } = IndentPriority.Routine;
    /// <summary>Optional HIS reference (admission/encounter number) — never a patient name.</summary>
    public string? EncounterRef { get; set; }
    public string? Notes { get; set; }
    public Guid? RequestedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? FirstIssuedAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }
    public List<WardIndentLine> Lines { get; set; } = [];
}

public class WardIndentLine : TenantEntity
{
    public Guid WardIndentId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal IssuedUnits { get; set; }
}

public enum WardIssueStatus { PendingCheck = 1, Issued = 2, Cancelled = 3 }

/// <summary>Stock issued from the pharmacy store to a department. Leaves stock (FEFO) when issued; cost is charged to the department.</summary>
public class WardIssue : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid DepartmentId { get; set; }
    public Department? Department { get; set; }
    public Guid? WardIndentId { get; set; }
    public string IssueNo { get; set; } = "";
    public WardIssueStatus Status { get; set; }
    /// <summary>True when any line is high-alert or a narcotic/Schedule X item: a different pharmacist must verify before stock moves.</summary>
    public bool NeedsSecondCheck { get; set; }
    public Guid PreparedByUserId { get; set; }
    public DateTimeOffset PreparedAt { get; set; }
    public Guid? CheckedByUserId { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public string? ReceivedBy { get; set; }
    public decimal CostValue { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }
    public List<WardIssueLine> Lines { get; set; } = [];
}

public class WardIssueLine : TenantEntity
{
    public Guid WardIssueId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Requested quantity; batches are picked (FEFO) when the issue is released.</summary>
    public decimal QtyUnits { get; set; }
    public Guid? WardIndentLineId { get; set; }
    public List<WardIssueBatch> Batches { get; set; } = [];
}

/// <summary>Which batches a released issue line came from.</summary>
public class WardIssueBatch : TenantEntity
{
    public Guid WardIssueLineId { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal CostPerUnit { get; set; }
    public decimal ReturnedUnits { get; set; }
}

public enum WardReturnReason { Unused = 1, Expired = 2, Damaged = 3 }

/// <summary>Goods coming back from a ward. Unused goods go back to stock and the department is credited; expired/damaged are recorded only.</summary>
public class WardReturn : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid DepartmentId { get; set; }
    public Guid WardIssueId { get; set; }
    public string ReturnNo { get; set; } = "";
    public WardReturnReason Reason { get; set; }
    public string? Notes { get; set; }
    public decimal CostValue { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<WardReturnLine> Lines { get; set; } = [];
}

public class WardReturnLine : TenantEntity
{
    public Guid WardReturnId { get; set; }
    public Guid WardIssueBatchId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal CostPerUnit { get; set; }
}
