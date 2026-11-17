using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>Supplier invoice + goods receipt (combined for MVP). Draft is editable; Posted moves stock.</summary>
public class PurchaseInvoice : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    /// <summary>Optional: the order this delivery fulfils.</summary>
    public Guid? PurchaseOrderId { get; set; }
    public string SupplierInvoiceNo { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal OtherCharges { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetAmount { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? PostedAt { get; set; }
    public Guid? PostedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public string? CancelReason { get; set; }
    public int Version { get; set; }
    public List<PurchaseInvoiceLine> Lines { get; set; } = [];
}

public class PurchaseInvoiceLine : TenantEntity
{
    public Guid PurchaseInvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string BatchNo { get; set; } = "";
    public DateOnly ExpiryDate { get; set; }
    public decimal Mrp { get; set; }
    /// <summary>Rate per pack, before tax.</summary>
    public decimal Rate { get; set; }
    public decimal QtyPacks { get; set; }
    public decimal FreeQtyPacks { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GstRate { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public Guid? BatchId { get; set; }
}

/// <summary>Retail sale. Created directly in Posted state; corrections only via cancel or return.</summary>
public class SalesInvoice : TenantEntity
{
    public Guid BranchId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateTimeOffset InvoiceAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public Guid? CustomerId { get; set; }
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? DoctorName { get; set; }
    public SalesStatus Status { get; set; } = SalesStatus.Posted;
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetAmount { get; set; }
    /// <summary>Client-generated idempotency key: retrying the same submit returns the same invoice.</summary>
    public Guid ClientRequestId { get; set; }
    /// <summary>Bill was made while the counter was offline and synced later; InvoiceAt is the capture time.</summary>
    public bool CapturedOffline { get; set; }
    /// <summary>Delivery charge on this bill (GST inclusive) and its split; included in TaxableAmount/TaxAmount/NetAmount.</summary>
    public decimal DeliveryCharge { get; set; }
    public decimal DeliveryChargeTaxable { get; set; }
    public decimal DeliveryChargeTax { get; set; }
    public decimal DeliveryChargeGstRate { get; set; }
    /// <summary>Provisional reference printed on the offline receipt (device + local sequence).</summary>
    public string? OfflineRef { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledByUserId { get; set; }
    public string? CancelReason { get; set; }
    public List<SalesInvoiceLine> Lines { get; set; } = [];
    public List<SalesPayment> Payments { get; set; } = [];
}

public class SalesInvoiceLine : TenantEntity
{
    public Guid SalesInvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal MrpPerUnit { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal NetAmount { get; set; }
    public decimal CostPerUnit { get; set; }
    public decimal ReturnedQtyUnits { get; set; }
}

public class SalesPayment : TenantEntity
{
    public Guid SalesInvoiceId { get; set; }
    public PaymentMode Mode { get; set; }
    public decimal Amount { get; set; }
    /// <summary>Card/UPI reference typed by staff. Not proof of settlement.</summary>
    public string? Reference { get; set; }
}

public class SalesReturn : TenantEntity
{
    public Guid BranchId { get; set; }
    public string ReturnNo { get; set; } = "";
    public Guid SalesInvoiceId { get; set; }
    public DateTimeOffset ReturnAt { get; set; }
    public DateOnly BusinessDate { get; set; }
    public string Reason { get; set; } = "";
    public PaymentMode RefundMode { get; set; }
    public decimal RefundAmount { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<SalesReturnLine> Lines { get; set; } = [];
}

public class SalesReturnLine : TenantEntity
{
    public Guid SalesReturnId { get; set; }
    public Guid SalesInvoiceLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }
    public decimal QtyUnits { get; set; }
    public decimal Amount { get; set; }
}
