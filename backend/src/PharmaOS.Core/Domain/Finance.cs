using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>
/// Goods sent back to a supplier (expiry, damage, recall) and the claim against them.
/// Stock leaves on Dispatch. Settlement records the supplier's credit note; any shortfall is written off.
/// </summary>
public class PurchaseReturn : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string ReturnNo { get; set; } = "";
    public PurchaseReturnReason Reason { get; set; }
    public PurchaseReturnStatus Status { get; set; } = PurchaseReturnStatus.Draft;
    /// <summary>Expected credit (incl. GST) at cost; what we are claiming.</summary>
    public decimal ClaimAmount { get; set; }
    /// <summary>Amount on the supplier's credit note.</summary>
    public decimal AcceptedAmount { get; set; }
    /// <summary>Claim − accepted, recognised as loss at settlement.</summary>
    public decimal WrittenOffAmount { get; set; }
    public string? SupplierCreditNoteNo { get; set; }
    public DateOnly? CreditNoteDate { get; set; }
    public string? Notes { get; set; }
    public string? RejectReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? DispatchedAt { get; set; }
    public DateTimeOffset? SettledAt { get; set; }
    public int Version { get; set; }
    public List<PurchaseReturnLine> Lines { get; set; } = [];
}

public class PurchaseReturnLine : TenantEntity
{
    public Guid PurchaseReturnId { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal CostPerUnit { get; set; }
    public decimal GstRate { get; set; }
    public decimal ClaimAmount { get; set; }
}

/// <summary>Money paid to a supplier, optionally allocated against specific purchase invoices.</summary>
public class SupplierPayment : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string PaymentNo { get; set; } = "";
    public DateOnly PaymentDate { get; set; }
    public SettlementMode Mode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public string? Notes { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Posted;
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<SupplierPaymentAllocation> Allocations { get; set; } = [];
}

public class SupplierPaymentAllocation : TenantEntity
{
    public Guid SupplierPaymentId { get; set; }
    public Guid PurchaseInvoiceId { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>Money received from a credit customer, allocated against their credit invoices.</summary>
public class CustomerReceipt : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string ReceiptNo { get; set; } = "";
    public DateOnly ReceiptDate { get; set; }
    public SettlementMode Mode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public DocStatus Status { get; set; } = DocStatus.Posted;
    public string? CancelReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<CustomerReceiptAllocation> Allocations { get; set; } = [];
}

public class CustomerReceiptAllocation : TenantEntity
{
    public Guid CustomerReceiptId { get; set; }
    public Guid SalesInvoiceId { get; set; }
    public decimal Amount { get; set; }
}
