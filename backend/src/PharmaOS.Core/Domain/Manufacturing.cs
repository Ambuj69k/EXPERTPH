using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

// Manufacturing & quality — a minimal slice: approved recipes (bill of materials), production batches that consume material
// batches (genealogy), QC specifications and results, and QA batch release with an electronic signature.
// NOT validated for GMP use: computer system validation (IQ/OQ/PQ), document control and training records are out of scope.

public enum MfgDocStatus { Draft = 1, Approved = 2, Retired = 3 }

/// <summary>Bill of materials for one batch of a finished product. Approved versions are frozen; changes need a new version.</summary>
public class Recipe : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int VersionNo { get; set; }
    public decimal BatchSizeUnits { get; set; }
    public MfgDocStatus Status { get; set; } = MfgDocStatus.Draft;
    public string? Notes { get; set; }
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public List<RecipeLine> Lines { get; set; } = [];
}

public class RecipeLine : TenantEntity
{
    public Guid RecipeId { get; set; }
    public Guid MaterialProductId { get; set; }
    public Product? Material { get; set; }
    /// <summary>Base units of the material for one full batch.</summary>
    public decimal QtyUnits { get; set; }
}

/// <summary>QC specification for a product (material or finished good): the tests a sample must pass.</summary>
public class QcSpec : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int VersionNo { get; set; }
    public MfgDocStatus Status { get; set; } = MfgDocStatus.Draft;
    public Guid? ApprovedByUserId { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public List<QcSpecTest> Tests { get; set; } = [];
}

public class QcSpecTest : TenantEntity
{
    public Guid QcSpecId { get; set; }
    public int Seq { get; set; }
    public string Name { get; set; } = "";
    public string? Unit { get; set; }
    /// <summary>Numeric limits (inclusive). Leave both empty for a pass/fail (text) test.</summary>
    public decimal? Min { get; set; }
    public decimal? Max { get; set; }
    public string? Requirement { get; set; }
}

public enum ProductionStatus { Planned = 1, InProcess = 2, Quarantine = 3, Released = 4, Rejected = 5, Cancelled = 6 }

/// <summary>
/// One manufacturing batch. Materials leave stock when issued (FEFO, released lots only); the output enters stock as a
/// blocked (quarantined) batch and can be sold only after QA release.
/// </summary>
public class ProductionBatch : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid RecipeId { get; set; }
    public Recipe? Recipe { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string BatchNo { get; set; } = "";
    public DateOnly MfgDate { get; set; }
    public DateOnly ExpiryDate { get; set; }
    public decimal Mrp { get; set; }
    public decimal PlannedUnits { get; set; }
    public ProductionStatus Status { get; set; } = ProductionStatus.Planned;
    public decimal MaterialCost { get; set; }
    public decimal OutputUnits { get; set; }
    public Guid? OutputBatchId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? MaterialsIssuedAt { get; set; }
    public DateTimeOffset? ProducedAt { get; set; }
    public Guid? ReleasedByUserId { get; set; }
    public DateTimeOffset? DecisionAt { get; set; }
    /// <summary>Printed meaning of the QA e-signature, e.g. "Released for sale by QA".</summary>
    public string? DecisionSignature { get; set; }
    public string? DecisionNote { get; set; }
    public int Version { get; set; }
    public List<MaterialConsumption> Materials { get; set; } = [];
}

/// <summary>Genealogy: which material lot (and how much) went into a production batch.</summary>
public class MaterialConsumption : TenantEntity
{
    public Guid ProductionBatchId { get; set; }
    public Guid MaterialProductId { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal CostPerUnit { get; set; }
}

public enum QcSampleStatus { Pending = 1, Passed = 2, Failed = 3 }

/// <summary>A QC sample of one inventory batch (incoming material or finished good) tested against a spec version.</summary>
public class QcSample : TenantEntity
{
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid QcSpecId { get; set; }
    public QcSpec? Spec { get; set; }
    public string SampleNo { get; set; } = "";
    public QcSampleStatus Status { get; set; } = QcSampleStatus.Pending;
    public Guid? TestedByUserId { get; set; }
    public DateTimeOffset? TestedAt { get; set; }
    public string? TesterSignature { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<QcResult> Results { get; set; } = [];
}

/// <summary>One recorded result. Results are written once with the analyst's signature; corrections need a new sample.</summary>
public class QcResult : TenantEntity, IImmutable
{
    public Guid QcSampleId { get; set; }
    public Guid QcSpecTestId { get; set; }
    public decimal? Value { get; set; }
    public string? Text { get; set; }
    public bool Pass { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
}

public enum DeviationSeverity { Minor = 1, Major = 2, Critical = 3 }
public enum DeviationStatus { Open = 1, CapaInProgress = 2, Closed = 3 }

/// <summary>
/// A deviation from procedure or specification, with root cause and corrective/preventive action (CAPA).
/// An open Major/Critical deviation linked to a batch blocks that batch's release. Closure is a QA e-signature.
/// </summary>
public class Deviation : TenantEntity, IVersioned
{
    public string DeviationNo { get; set; } = "";
    public Guid? ProductionBatchId { get; set; }
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public DeviationSeverity Severity { get; set; }
    public DeviationStatus Status { get; set; } = DeviationStatus.Open;
    public string? RootCause { get; set; }
    public string? CorrectiveAction { get; set; }
    public string? PreventiveAction { get; set; }
    public Guid? CapaOwnerUserId { get; set; }
    public DateOnly? CapaDueDate { get; set; }
    public Guid RaisedByUserId { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public string? ClosureSignature { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public int Version { get; set; }
}
