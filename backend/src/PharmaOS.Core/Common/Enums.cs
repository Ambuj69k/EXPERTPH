namespace PharmaOS.Core.Common;

public enum UserRole
{
    Owner = 1,
    Pharmacist = 2,
    Cashier = 3,
    PurchaseClerk = 4,
    /// <summary>Books and reports (read), manual journals; no billing or stock actions.</summary>
    Accountant = 5,
    /// <summary>Sees only deliveries assigned to them; marks delivered (with handover code) or failed.</summary>
    DeliveryAgent = 6,
    /// <summary>Distributor field sales: takes orders and collections for assigned retailers.</summary>
    Salesman = 7,
    /// <summary>Medical representative: own doctors, tour plan, daily call reports, sample bag.</summary>
    MedicalRep = 8,
    /// <summary>Area manager: approves tour plans and daily reports of MRs in the territories they supervise.</summary>
    AreaManager = 9,
    /// <summary>Hospital ward/department staff: raise indents and returns for their own department only.</summary>
    WardStaff = 10,
    /// <summary>Manufacturing: plans batches, issues materials, records output.</summary>
    Production = 11,
    /// <summary>QC lab: samples and test results.</summary>
    QcAnalyst = 12,
    /// <summary>QA: approves recipes/specs and releases or rejects batches.</summary>
    QaManager = 13,
}

/// <summary>
/// Regulatory classification label. Stored for record-keeping and UI warnings only;
/// the applicable schedules and their obligations must be verified against current rules.
/// </summary>
public enum DrugSchedule
{
    None = 0,
    H = 1,
    H1 = 2,
    X = 3,
    NarcoticPsychotropic = 4,
    G = 5,
}

public enum PurchaseStatus
{
    Draft = 1,
    Posted = 2,
    Cancelled = 3,
}

public enum SalesStatus
{
    Posted = 2,
    Cancelled = 3,
}

public enum PaymentMode
{
    Cash = 1,
    Card = 2,
    Upi = 3,
    Credit = 4,
    /// <summary>Paid with loyalty points (valued at the tenant's point value).</summary>
    Loyalty = 5,
}

public enum ConsentChannel { Sms = 1, WhatsApp = 2, Email = 3, Call = 4 }

public enum ConsentPurpose
{
    /// <summary>Refill / pickup reminders the customer asked for.</summary>
    Reminders = 1,
    Promotions = 2,
}

public enum ReminderStatus { Active = 1, Paused = 2, Cancelled = 3 }

public enum NotificationStatus
{
    /// <summary>Message prepared; waiting for staff/provider to send.</summary>
    Pending = 1,
    Sent = 2,
    Failed = 3,
    /// <summary>Not sent (no consent, no number, opted out).</summary>
    Skipped = 4,
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    Sent = 2,
    PartiallyReceived = 3,
    Received = 4,
    /// <summary>Closed short: no more deliveries expected.</summary>
    Closed = 5,
    Cancelled = 6,
}

public enum LoyaltyEntryType { Earn = 1, Redeem = 2, ReverseEarn = 3, ReverseRedeem = 4, Adjust = 5 }

public enum MovementType
{
    Opening = 1,
    PurchaseReceipt = 2,
    PurchaseCancel = 3,
    Sale = 4,
    SaleCancel = 5,
    SaleReturn = 6,
    AdjustmentIn = 7,
    AdjustmentOut = 8,
    PurchaseReturnOut = 9,
    PurchaseReturnRejectedIn = 10,
    CountGain = 11,
    CountLoss = 12,
    TransferOut = 13,
    TransferIn = 14,
    TradeSale = 15,
    TradeSaleCancel = 16,
    TradeReturnIn = 17,
    WardIssue = 18,
    WardReturnIn = 19,
    ProductionConsume = 20,
    ProductionOutput = 21,
}

/// <summary>Draft → Dispatched (left source, in transit) → Received (at destination; shortages recorded) | Cancelled (draft only).</summary>
public enum TransferStatus { Draft = 1, Dispatched = 2, Received = 3, Cancelled = 4 }

public enum IndentStatus { Requested = 1, Fulfilled = 2, Rejected = 3 }

public enum ShiftStatus
{
    Open = 1,
    Closed = 2,
}

public enum CashMovementKind
{
    PayIn = 1,
    PayOut = 2,
}

public enum StockCountStatus
{
    InProgress = 1,
    Posted = 2,
    Cancelled = 3,
}

public enum PurchaseReturnReason
{
    Expired = 1,
    NearExpiry = 2,
    Damaged = 3,
    Recall = 4,
    Other = 5,
}

/// <summary>Draft → Dispatched (stock leaves) → Settled (credit note) | Rejected (goods come back).</summary>
public enum PurchaseReturnStatus
{
    Draft = 1,
    Dispatched = 2,
    Settled = 3,
    Rejected = 4,
}

/// <summary>How money moved for supplier payments and customer receipts.</summary>
public enum SettlementMode
{
    Cash = 1,
    BankTransfer = 2,
    Upi = 3,
    Cheque = 4,
    Card = 5,
}

public enum DocStatus
{
    Posted = 2,
    Cancelled = 3,
}
