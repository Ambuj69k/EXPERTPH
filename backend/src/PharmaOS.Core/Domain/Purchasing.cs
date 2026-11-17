using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>Order placed with a supplier. Received through one or more linked purchase invoices.</summary>
public class PurchaseOrder : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string PoNo { get; set; } = "";
    public DateOnly OrderDate { get; set; }
    public DateOnly? ExpectedDate { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public int Version { get; set; }
    public List<PurchaseOrderLine> Lines { get; set; } = [];
}

public class PurchaseOrderLine : TenantEntity
{
    public Guid PurchaseOrderId { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal QtyPacks { get; set; }
    /// <summary>Paid packs received so far via linked purchase invoices (free goods don't count).</summary>
    public decimal ReceivedQtyPacks { get; set; }
    public decimal? ExpectedRate { get; set; }
}

/// <summary>How a supplier names/codes a product on their invoices → our product. Learnt from confirmed imports.</summary>
public class SupplierProductMap : TenantEntity
{
    public Guid SupplierId { get; set; }
    public string? SupplierItemCode { get; set; }
    /// <summary>Normalised (upper-case, single-spaced, no punctuation) supplier item name.</summary>
    public string SupplierItemName { get; set; } = "";
    public Guid ProductId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
