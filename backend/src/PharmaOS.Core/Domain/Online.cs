using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

public enum FulfilmentType { Pickup = 1, Delivery = 2 }

/// <summary>
/// Submitted → Accepted | NeedsCustomerApproval → Accepted → Packed (billed; stock leaves) → ReadyForPickup | OutForDelivery
/// → Completed. Side exits: Rejected, Cancelled, DeliveryFailed (→ OutForDelivery again or Cancelled).
/// </summary>
public enum OnlineOrderStatus
{
    Submitted = 1,
    NeedsCustomerApproval = 2,
    Accepted = 3,
    Packed = 4,
    ReadyForPickup = 5,
    OutForDelivery = 6,
    Completed = 7,
    DeliveryFailed = 8,
    Rejected = 9,
    Cancelled = 10,
}

/// <summary>
/// Order placed through a pharmacy's ordering page. Every order is reviewed by a pharmacist before stock is committed.
/// Payment is collected on delivery/pickup (no online payment provider configured).
/// </summary>
public class OnlineOrder : TenantEntity
{
    public Guid BranchId { get; set; }
    public string OrderNo { get; set; } = "";
    /// <summary>Unguessable token in the customer's tracking link (customers have no login).</summary>
    public string TrackingToken { get; set; } = "";
    /// <summary>Shown to the customer; the delivery agent must enter it to prove handover.</summary>
    public string HandoverCode { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string? Address { get; set; }
    public string? Pincode { get; set; }
    public FulfilmentType Fulfilment { get; set; }
    public OnlineOrderStatus Status { get; set; } = OnlineOrderStatus.Submitted;
    /// <summary>Free-text request for prescription items that are not publicly listed.</summary>
    public string? RequestText { get; set; }
    public string? CustomerNotes { get; set; }
    public string? PharmacistNotes { get; set; }
    /// <summary>Prescriber as written on the uploaded prescription (needed for scheduled drugs).</summary>
    public string? PrescriberName { get; set; }
    public string? RejectReason { get; set; }
    public Guid? ReviewedByUserId { get; set; }
    public Guid? CustomerId { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? DeliveryAgentId { get; set; }
    public int DeliveryAttempts { get; set; }
    public string? DeliveryFailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OnlineOrderLine> Lines { get; set; } = [];
    public List<OnlineOrderFile> Files { get; set; } = [];
}

public class OnlineOrderLine : TenantEntity
{
    public Guid OnlineOrderId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public decimal RequestedQtyUnits { get; set; }
    /// <summary>Set by the pharmacist on review; 0 = not supplied.</summary>
    public decimal? ApprovedQtyUnits { get; set; }
    /// <summary>True when the pharmacist added this line (e.g. from a prescription request).</summary>
    public bool AddedByPharmacist { get; set; }
}

/// <summary>Uploaded prescription image/PDF. Stored on disk under a random name; only staff can read it.</summary>
public class OnlineOrderFile : TenantEntity
{
    public Guid OnlineOrderId { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string StoragePath { get; set; } = "";
    public DateTimeOffset UploadedAt { get; set; }
}
