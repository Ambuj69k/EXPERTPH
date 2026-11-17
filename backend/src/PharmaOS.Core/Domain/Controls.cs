using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>
/// A cashier's drawer session. Expected cash is derived from the documents that user posted during the shift;
/// at close the counted cash is recorded and the variance is kept for review.
/// </summary>
public class CashShift : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid UserId { get; set; }
    public string ShiftNo { get; set; } = "";
    public ShiftStatus Status { get; set; } = ShiftStatus.Open;
    public DateTimeOffset OpenedAt { get; set; }
    public decimal OpeningCash { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public decimal? ExpectedCash { get; set; }
    public decimal? CountedCash { get; set; }
    public decimal? Variance { get; set; }
    public string? CloseNotes { get; set; }
    public List<CashMovement> Movements { get; set; } = [];
}

/// <summary>Petty cash in/out during a shift (e.g. tea, courier, change brought from bank).</summary>
public class CashMovement : TenantEntity, IImmutable
{
    public Guid CashShiftId { get; set; }
    public CashMovementKind Kind { get; set; }
    public decimal Amount { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset At { get; set; }
    public Guid? UserId { get; set; }
}

/// <summary>Physical stock count for a set of products. Variances post as CountGain/CountLoss movements.</summary>
public class StockCount : TenantEntity
{
    public Guid BranchId { get; set; }
    public string CountNo { get; set; } = "";
    public string Title { get; set; } = "";
    public StockCountStatus Status { get; set; } = StockCountStatus.InProgress;
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedByUserId { get; set; }
    public List<StockCountLine> Lines { get; set; } = [];
}

public class StockCountLine : TenantEntity
{
    public Guid StockCountId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    /// <summary>System quantity captured at the moment the counted figure was entered (not at count creation),
    /// so sales during a long count don't distort the variance.</summary>
    public decimal? SystemQtyAtCount { get; set; }
    public decimal? CountedQty { get; set; }
    public DateTimeOffset? CountedAt { get; set; }
    public decimal CostPerUnit { get; set; }
}
