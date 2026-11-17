using PharmaOS.Core.Common;

namespace PharmaOS.Core.Domain;

public enum AccountType
{
    Asset = 1,
    Liability = 2,
    Equity = 3,
    Income = 4,
    Expense = 5,
}

/// <summary>Chart of accounts. System accounts (SystemKey set) are used by the posting engine and cannot be deleted.</summary>
public class LedgerAccount : TenantEntity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public AccountType Type { get; set; }
    /// <summary>Stable key used by automatic postings, e.g. "CASH", "AR", "GST_OUT".</summary>
    public string? SystemKey { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// One balanced voucher. Created by the posting engine from a business document event, or manually.
/// Immutable: corrections are reversal entries.
/// </summary>
public class JournalEntry : TenantEntity, IImmutable
{
    public Guid BranchId { get; set; }
    public string EntryNo { get; set; } = "";
    public DateOnly Date { get; set; }
    /// <summary>Document type ("SalesInvoice", "PurchaseInvoice", …) or "Manual".</summary>
    public string SourceType { get; set; } = "";
    public Guid SourceId { get; set; }
    /// <summary>Document event this entry represents: "post", "cancel", "dispatch", "settle", "close", "manual", "reverse".</summary>
    public string Event { get; set; } = "";
    public string? SourceNo { get; set; }
    public string Narration { get; set; } = "";
    public Guid? ReversesEntryId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<JournalLine> Lines { get; set; } = [];
}

public class JournalLine : TenantEntity, IImmutable
{
    public Guid JournalEntryId { get; set; }
    public Guid AccountId { get; set; }
    public LedgerAccount? Account { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    /// <summary>Supplier or customer for receivable/payable lines (sub-ledger link).</summary>
    public Guid? PartyId { get; set; }
}
