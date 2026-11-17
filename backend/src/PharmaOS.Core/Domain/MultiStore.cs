using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>
/// Stock moved between two branches of the SAME business (same legal entity). Stock leaves the source on dispatch,
/// is "in transit" until the destination receives it, and any shortfall is recorded as a transit loss.
/// Transfers between different legal entities must be done as a sale + purchase instead (not modelled here).
/// </summary>
public class StockTransfer : TenantEntity
{
    public string TransferNo { get; set; } = "";
    public Guid FromBranchId { get; set; }
    public Guid ToBranchId { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Draft;
    public Guid? IndentId { get; set; }
    public string? Notes { get; set; }
    public string? ReceiveNotes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public Guid? DispatchedByUserId { get; set; }
    public DateTimeOffset? ReceivedAt { get; set; }
    public Guid? ReceivedByUserId { get; set; }
    public List<StockTransferLine> Lines { get; set; } = [];
}

public class StockTransferLine : TenantEntity
{
    public Guid StockTransferId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal? ReceivedQtyUnits { get; set; }
    /// <summary>Cost per unit at dispatch (for valuing transit and shortages).</summary>
    public decimal CostPerUnit { get; set; }
}

/// <summary>A store asking another branch/warehouse for stock. Fulfilled by a transfer.</summary>
public class StockIndent : TenantEntity
{
    public string IndentNo { get; set; } = "";
    /// <summary>The branch asking for stock.</summary>
    public Guid RequestingBranchId { get; set; }
    /// <summary>The branch/warehouse asked to supply.</summary>
    public Guid SupplyingBranchId { get; set; }
    public IndentStatus Status { get; set; } = IndentStatus.Requested;
    public string? Notes { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<StockIndentLine> Lines { get; set; } = [];
}

public class StockIndentLine : TenantEntity
{
    public Guid StockIndentId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal QtyUnits { get; set; }
}
