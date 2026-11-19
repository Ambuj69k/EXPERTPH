using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>A retail pharmacy (or other licensed buyer) that a distributor sells to on credit.</summary>
public class Retailer : TenantEntity, IVersioned
{
    public string Name { get; set; } = "";
    public string? Gstin { get; set; }
    /// <summary>Two-digit GST state code; decides IGST vs CGST+SGST.</summary>
    public string StateCode { get; set; } = "";
    /// <summary>Stored for records only — the software does not verify licences.</summary>
    public string? DrugLicenseNo { get; set; }
    public DateOnly? DrugLicenseValidTo { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Route { get; set; }
    public decimal CreditLimit { get; set; }
    public int CreditDays { get; set; } = 30;
    public Guid? SalesmanUserId { get; set; }
    public bool IsActive { get; set; } = true;
    public int Version { get; set; }
}

/// <summary>Trade rate (PTR) per pack, before GST. RetailerId null = default list for everyone.</summary>
public class TradeRate : TenantEntity
{
    public Guid ProductId { get; set; }
    public Guid? RetailerId { get; set; }
    public decimal RatePerPack { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Quantity scheme ("buy 10 get 1") and/or trade discount on a product for a period.</summary>
public class TradeScheme : TenantEntity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = "";
    public decimal BuyQtyPacks { get; set; }
    public decimal FreeQtyPacks { get; set; }
    public decimal DiscountPercent { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
}

public enum SalesOrderStatus { Open = 1, Invoiced = 2, Cancelled = 3 }

public class SalesOrder : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid RetailerId { get; set; }
    public Retailer? Retailer { get; set; }
    public string OrderNo { get; set; } = "";
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Open;
    public string? Notes { get; set; }
    public Guid? TakenByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? TradeInvoiceId { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = [];
}

public class SalesOrderLine : TenantEntity
{
    public Guid SalesOrderId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal QtyPacks { get; set; }
}

public enum TradeDocStatus { Posted = 2, Cancelled = 3 }

/// <summary>B2B tax invoice to a retailer. Stock leaves on posting (paid + free packs, FEFO). Corrections by credit note or cancel.</summary>
public class TradeInvoice : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid RetailerId { get; set; }
    public Retailer? Retailer { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public TradeDocStatus Status { get; set; } = TradeDocStatus.Posted;
    /// <summary>True when the retailer is in another state: tax is IGST instead of CGST+SGST.</summary>
    public bool InterState { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal RoundOff { get; set; }
    public decimal NetAmount { get; set; }
    public Guid? SalesOrderId { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CancelReason { get; set; }
    public DateTimeOffset? CancelledAt { get; set; }
    public List<TradeInvoiceLine> Lines { get; set; } = [];
}

public class TradeInvoiceLine : TenantEntity
{
    public Guid TradeInvoiceId { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid BatchId { get; set; }
    public Batch? Batch { get; set; }
    public decimal QtyPacks { get; set; }
    public decimal FreeQtyPacks { get; set; }
    public decimal RatePerPack { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal GrossAmount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal GstRate { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostPerUnit { get; set; }
    public decimal ReturnedPacks { get; set; }
    public Guid? SchemeId { get; set; }
}

public enum TradeReturnReason { Saleable = 1, Expired = 2, Damaged = 3, Other = 4 }

/// <summary>Credit note for goods a retailer returns. Saleable goods go back to stock; expired/damaged are written off.</summary>
public class TradeCreditNote : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid RetailerId { get; set; }
    public Guid TradeInvoiceId { get; set; }
    public string CreditNoteNo { get; set; } = "";
    public DateOnly Date { get; set; }
    public TradeReturnReason Reason { get; set; }
    public string? Notes { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Amount { get; set; }
    public decimal CostValue { get; set; }
    public Guid ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TradeCreditNoteLine> Lines { get; set; } = [];
}

public class TradeCreditNoteLine : TenantEntity
{
    public Guid TradeCreditNoteId { get; set; }
    public Guid TradeInvoiceLineId { get; set; }
    public Guid ProductId { get; set; }
    public Guid BatchId { get; set; }
    public decimal QtyPacks { get; set; }
    public decimal TaxableAmount { get; set; }
    public decimal TaxAmount { get; set; }
}

/// <summary>Money collected from a retailer (by office or salesman), allocated to trade invoices.</summary>
public class TradeReceipt : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid RetailerId { get; set; }
    public Retailer? Retailer { get; set; }
    public string ReceiptNo { get; set; } = "";
    public DateOnly Date { get; set; }
    public SettlementMode Mode { get; set; }
    public decimal Amount { get; set; }
    public string? Reference { get; set; }
    public TradeDocStatus Status { get; set; } = TradeDocStatus.Posted;
    public string? CancelReason { get; set; }
    public Guid? CollectedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<TradeReceiptAllocation> Allocations { get; set; } = [];
}

public class TradeReceiptAllocation : TenantEntity
{
    public Guid TradeReceiptId { get; set; }
    public Guid TradeInvoiceId { get; set; }
    public decimal Amount { get; set; }
}

public enum DispatchStatus { Packing = 1, OnRoute = 2, Completed = 3, Cancelled = 4 }
public enum DeliveryStatus { Pending = 1, Delivered = 2, Failed = 3 }

/// <summary>A vehicle trip: posted trade invoices are picked together (pick list), loaded, and each is confirmed delivered or failed.</summary>
public class Dispatch : TenantEntity, IVersioned
{
    public Guid BranchId { get; set; }
    public string DispatchNo { get; set; } = "";
    public DateOnly Date { get; set; }
    public string? Route { get; set; }
    public string? Vehicle { get; set; }
    public string? DriverName { get; set; }
    public DispatchStatus Status { get; set; } = DispatchStatus.Packing;
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? DepartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int Version { get; set; }
    public List<DispatchStop> Stops { get; set; } = [];
}

public class DispatchStop : TenantEntity
{
    public Guid DispatchId { get; set; }
    public Guid TradeInvoiceId { get; set; }
    public TradeInvoice? Invoice { get; set; }
    public int Seq { get; set; }
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;
    public string? ReceivedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset? At { get; set; }
}
