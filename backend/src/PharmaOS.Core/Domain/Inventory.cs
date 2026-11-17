using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>A manufacturer batch of a product. Identity = (Product, BatchNo, Mrp) because MRP can be revised within a batch.</summary>
public class Batch : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string BatchNo { get; set; } = "";
    /// <summary>Always normalised to the last day of the printed expiry month.</summary>
    public DateOnly ExpiryDate { get; set; }
    /// <summary>MRP per pack (tax inclusive).</summary>
    public decimal Mrp { get; set; }
    /// <summary>Last purchase rate per pack, before tax.</summary>
    public decimal PurchaseRate { get; set; }
    /// <summary>Effective cost per base unit (after discount and free goods, excluding GST).</summary>
    public decimal CostPerUnit { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Quarantined (recall, suspected quality issue): cannot be sold until unblocked.</summary>
    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
}

/// <summary>
/// Current on-hand quantity per branch+batch. This is a projection of StockMovement and is only
/// changed by InventoryService in the same transaction as the movement row.
/// </summary>
public class StockBalance : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public Guid ProductId { get; set; }
    public decimal QtyUnits { get; set; }
}

/// <summary>Immutable inventory ledger. Sum of QtyUnits per branch+batch must equal StockBalance.QtyUnits.</summary>
public class StockMovement : TenantEntity, IImmutable
{
    public Guid BranchId { get; set; }
    public Guid BatchId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QtyUnits { get; set; }
    public MovementType Type { get; set; }
    public string SourceType { get; set; } = "";
    public Guid SourceId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Opening stock or correction. Posted immediately; never edited.</summary>
public class StockAdjustment : TenantEntity
{
    public Guid BranchId { get; set; }
    public string AdjustmentNo { get; set; } = "";
    public bool IsOpening { get; set; }
    public string Reason { get; set; } = "";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<StockAdjustmentLine> Lines { get; set; } = [];
}

public class StockAdjustmentLine : TenantEntity
{
    public Guid StockAdjustmentId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }
    /// <summary>Signed change in base units.</summary>
    public decimal QtyUnits { get; set; }
}
