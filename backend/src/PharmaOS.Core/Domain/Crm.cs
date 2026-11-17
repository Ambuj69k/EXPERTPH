using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>
/// Append-only consent history. The current state for a channel+purpose is the latest record.
/// Opt-out is simply a record with Granted = false.
/// </summary>
public class ConsentRecord : TenantEntity, IImmutable
{
    public Guid CustomerId { get; set; }
    public ConsentChannel Channel { get; set; }
    public ConsentPurpose Purpose { get; set; }
    public bool Granted { get; set; }
    /// <summary>How it was captured, e.g. "verbal at counter", "signed form", "replied STOP".</summary>
    public string Source { get; set; } = "";
    public Guid? RecordedByUserId { get; set; }
    public DateTimeOffset At { get; set; }
}

/// <summary>
/// A reminder the customer explicitly asked for (e.g. "remind me when my monthly strip is due").
/// Stores only the product and interval — never a diagnosis.
/// </summary>
public class RefillReminder : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int IntervalDays { get; set; }
    public DateOnly NextDueDate { get; set; }
    public ReminderStatus Status { get; set; } = ReminderStatus.Active;
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset? LastContactedAt { get; set; }
}

public class NotificationTemplate : TenantEntity
{
    public string Key { get; set; } = "";
    public ConsentChannel Channel { get; set; }
    public string Body { get; set; } = "";
    public bool IsActive { get; set; } = true;
}

/// <summary>Every outbound message attempt, including ones skipped for lack of consent.</summary>
public class NotificationLog : TenantEntity
{
    public Guid BranchId { get; set; }
    public Guid? CustomerId { get; set; }
    public ConsentChannel Channel { get; set; }
    public ConsentPurpose Purpose { get; set; }
    public string TemplateKey { get; set; } = "";
    public string? Recipient { get; set; }
    public string Message { get; set; } = "";
    public NotificationStatus Status { get; set; }
    /// <summary>"manual" = staff sends from their own phone via click-to-chat; future: provider name.</summary>
    public string Provider { get; set; } = "manual";
    public string? ProviderRef { get; set; }
    public string? Error { get; set; }
    public string? RelatedEntityType { get; set; }
    public Guid? RelatedEntityId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? SentAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}

/// <summary>Immutable points ledger; balance = sum of Points.</summary>
public class LoyaltyEntry : TenantEntity, IImmutable
{
    public Guid CustomerId { get; set; }
    public LoyaltyEntryType Type { get; set; }
    public int Points { get; set; }
    public Guid? SalesInvoiceId { get; set; }
    public Guid? SalesReturnId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
}
