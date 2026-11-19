using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>A paying customer of the SaaS (one business/group). Not tenant-scoped itself.</summary>
public class Tenant : Entity
{
    public string Name { get; set; } = "";
    public string TimeZone { get; set; } = BusinessCalendar.DefaultTimeZone;
    /// <summary>Max line discount % a Cashier may give without owner/pharmacist.</summary>
    public decimal CashierMaxDiscountPercent { get; set; } = 10m;
    /// <summary>When on, a user must have an open cash shift to bill.</summary>
    public bool RequireOpenShift { get; set; }
    public bool LoyaltyEnabled { get; set; }
    /// <summary>Spend (₹) needed to earn one point.</summary>
    public decimal LoyaltyRupeesPerPoint { get; set; } = 100m;
    /// <summary>Value (₹) of one point when redeemed.</summary>
    public decimal LoyaltyPointValue { get; set; } = 1m;
    public int LoyaltyMinRedeemPoints { get; set; }
    /// <summary>Books are closed up to and including this date: no journal may be dated on or before it.</summary>
    public DateOnly? BooksLockedUntil { get; set; }
    /// <summary>Owner-granted window during which provider support may view (read-only) this account.</summary>
    public DateTimeOffset? SupportAccessUntil { get; set; }
    public DateTimeOffset? DeletionScheduledAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>A physical store. Stock and invoice numbering are per branch.</summary>
public class Branch : TenantEntity
{
    public string Name { get; set; } = "";
    /// <summary>Short code used in document numbers (max 4 chars, A-Z/0-9).</summary>
    public string Code { get; set; } = "";
    public string? Address { get; set; }
    public string? Phone { get; set; }
    public string? Gstin { get; set; }
    /// <summary>Two-digit GST state code, e.g. "27" Maharashtra.</summary>
    public string StateCode { get; set; } = "";
    /// <summary>Stored as a record only; the software does not verify licence validity.</summary>
    public string? DrugLicenseNo { get; set; }
    public DateOnly? DrugLicenseValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    /// <summary>Warehouse / central store: holds and transfers stock, no counter billing.</summary>
    public bool IsWarehouse { get; set; }
    public bool OnlineOrdersEnabled { get; set; }
    /// <summary>Comma-separated pincodes served for home delivery (empty = pickup only).</summary>
    public string? DeliveryPincodes { get; set; }
    public string? OnlineNotice { get; set; }
    /// <summary>Home-delivery charge per order, GST inclusive (0 = free delivery).</summary>
    public decimal DeliveryCharge { get; set; }
    /// <summary>Orders whose items total at least this get free delivery (null = never free).</summary>
    public decimal? FreeDeliveryAbove { get; set; }
    /// <summary>GST % on the delivery charge. Default 18 — CA must confirm (it may follow the goods as a composite supply).</summary>
    public decimal DeliveryChargeGstRate { get; set; } = 18m;
}

public class AppUser : TenantEntity
{
    public string Email { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; }
    public Guid DefaultBranchId { get; set; }
    /// <summary>Hospital ward staff: the department they raise indents for.</summary>
    public Guid? DepartmentId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Encrypted TOTP seed (ASP.NET Data Protection). Set during setup; MFA is active only once MfaEnabled is true.</summary>
    public string? MfaSecret { get; set; }
    public bool MfaEnabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

/// <summary>Gap-free-per-series document counter (per branch, doc type, financial year).</summary>
public class DocumentSequence : TenantEntity
{
    public Guid BranchId { get; set; }
    public string DocType { get; set; } = "";
    public string FinancialYear { get; set; } = "";
    public long NextNumber { get; set; } = 1;
}

/// <summary>Append-only business audit log.</summary>
public class AuditEvent : TenantEntity, IImmutable
{
    public Guid? UserId { get; set; }
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    public string? Summary { get; set; }
    public DateTimeOffset At { get; set; }
}
