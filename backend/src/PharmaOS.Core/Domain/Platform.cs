using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

/// <summary>SaaS provider staff. Completely separate from tenant users; never tenant-scoped.</summary>
public class PlatformUser : Entity
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Encrypted TOTP seed (ASP.NET Data Protection). Set during setup; MFA is active only once MfaEnabled is true.</summary>
    public string? MfaSecret { get; set; }
    public bool MfaEnabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTimeOffset? LockedUntil { get; set; }
}

public enum SubscriptionStatus { Trial = 1, Active = 2, PastDue = 3, Cancelled = 4 }
public enum MigrationStatus { NotStarted = 1, InProgress = 2, Completed = 3, NotNeeded = 4 }
public enum PlatformInvoiceStatus { Due = 1, Paid = 2, Void = 3 }

/// <summary>Provider-side record of what a tenant has bought. One per tenant.</summary>
public class Subscription : Entity
{
    public Guid TenantId { get; set; }
    public string PlanCode { get; set; } = "";
    public SubscriptionStatus Status { get; set; }
    public DateTimeOffset? TrialEndsAt { get; set; }
    public DateOnly? PaidUntil { get; set; }
    /// <summary>Comma-separated module keys added on top of / removed from the plan (feature flags).</summary>
    public string? ExtraModules { get; set; }
    public string? DisabledModules { get; set; }
    public int? MaxBranchesOverride { get; set; }
    public int? MaxUsersOverride { get; set; }
    public MigrationStatus Migration { get; set; } = MigrationStatus.NotStarted;
    public string? Notes { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>Provider's bill to a tenant. Payment is recorded manually (no payment gateway configured).</summary>
public class PlatformInvoice : Entity
{
    public Guid TenantId { get; set; }
    public string InvoiceNo { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public decimal Amount { get; set; }
    public DateOnly DueDate { get; set; }
    public PlatformInvoiceStatus Status { get; set; } = PlatformInvoiceStatus.Due;
    public DateTimeOffset? PaidAt { get; set; }
    public string? PaymentReference { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public enum TicketStatus { Open = 1, WaitingOnCustomer = 2, Closed = 3 }

public class SupportTicket : TenantEntity
{
    public string Subject { get; set; } = "";
    public TicketStatus Status { get; set; } = TicketStatus.Open;
    public string Priority { get; set; } = "normal";
    public Guid? CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<SupportMessage> Messages { get; set; } = [];
}

public class SupportMessage : TenantEntity
{
    public Guid SupportTicketId { get; set; }
    public bool FromProvider { get; set; }
    public string AuthorName { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTimeOffset At { get; set; }
}
