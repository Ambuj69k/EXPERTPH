using System.Text;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record AccountDto(Guid Id, string Code, string Name, AccountType Type, string? SystemKey, bool IsActive);
public record AccountInput(string Code, string Name, AccountType Type, bool IsActive);
public record SyncResult(int EntriesCreated, DateTimeOffset At);
public record JournalLineInput(Guid AccountId, decimal Debit, decimal Credit);
public record ManualJournalInput(DateOnly Date, string Narration, List<JournalLineInput> Lines);
public record JournalLineDto(Guid AccountId, string AccountCode, string AccountName, decimal Debit, decimal Credit, Guid? PartyId);
public record JournalDto(Guid Id, string EntryNo, DateOnly Date, string SourceType, string Event, string? SourceNo, Guid SourceId,
    string Narration, Guid? ReversesEntryId, decimal Total, List<JournalLineDto> Lines);
public record TrialBalanceRow(Guid AccountId, string Code, string Name, AccountType Type, decimal Debit, decimal Credit, decimal Balance);
public record TrialBalance(DateOnly AsOf, List<TrialBalanceRow> Rows, decimal TotalDebit, decimal TotalCredit, bool Balanced);
public record StatementRow(string Code, string Name, decimal Amount);
public record ProfitAndLoss(DateOnly From, DateOnly To, List<StatementRow> Income, decimal TotalIncome, List<StatementRow> Expenses,
    decimal TotalExpenses, decimal NetProfit);
public record BalanceSheet(DateOnly AsOf, List<StatementRow> Assets, decimal TotalAssets, List<StatementRow> Liabilities, decimal TotalLiabilities,
    List<StatementRow> Equity, decimal CurrentEarnings, decimal TotalEquity, bool Balanced);
public record LedgerRow(DateOnly Date, string EntryNo, string SourceType, string? SourceNo, string Narration, decimal Debit, decimal Credit, decimal Balance);
public record AccountLedger(Guid AccountId, string Code, string Name, DateOnly From, DateOnly To, decimal Opening, List<LedgerRow> Rows, decimal Closing);
public record GstRateRow(decimal Rate, decimal Taxable, decimal Tax);
public record GstSummary(DateOnly From, DateOnly To, List<GstRateRow> OutwardSupplies, decimal OutputTax, List<GstRateRow> InwardSupplies,
    decimal InputTax, decimal ReturnsToSupplierTax, decimal NetTax);
public record ControlCheck(string Name, decimal Ledger, decimal SubLedger, decimal Difference, string Note);
public record CloseRequest(DateOnly UpTo);

/// <summary>
/// Double-entry general ledger generated from business documents (perpetual inventory).
///
/// Posting rules — ENGINEERING DEFAULTS, MUST BE REVIEWED BY A CHARTERED ACCOUNTANT:
///   Purchase posted      Dr Inventory (taxable) + Dr GST Input (tax) + Dr Freight (other charges) ± Round off   Cr Supplier Payables (net)
///   Sale posted          Dr Cash | Card/UPI Clearing | Customer Receivables | Loyalty Discounts (per payment)  Cr Sales (taxable) + Cr GST Output (tax) ± Round off
///                        Dr Cost of Goods Sold  Cr Inventory (qty × cost captured on the sale line)
///   Sales return         Dr Sales Returns (taxable) + Dr GST Output (tax)  Cr Cash | Clearing | Receivables (refund mode)
///                        Dr Inventory  Cr COGS (returned qty × original line cost)
///   Customer receipt     Dr Cash | Clearing (UPI/Card) | Bank (transfer/cheque)  Cr Customer Receivables
///   Supplier payment     Dr Supplier Payables  Cr Cash | Bank
///   Return to supplier   dispatch: Dr Supplier Claims (claim) Cr Inventory (cost) + Cr GST Input (tax in claim)
///                        settle:   Dr Supplier Payables (credit note) + Dr Claims Written Off (shortfall)  Cr Supplier Claims
///                        reject:   reversal of dispatch
///   Opening stock        Dr Inventory  Cr Opening Balance Equity
///   Adjustment/count     gain: Dr Inventory Cr Inventory Gain · loss: Dr Stock Loss Cr Inventory (qty × batch cost)
///   Petty cash           pay-out: Dr Petty Expenses Cr Cash · pay-in: Dr Cash Cr Bank (assumed change drawn from bank)
///   Shift close          variance: Dr/Cr Cash vs Cash Short/Over
///   Cancellations        exact reversal of the original entry
/// Loyalty points are expensed when redeemed (no liability is accrued at earning) — a simplification.
/// Events dated on/before the books-lock date are posted on the first open day, with a note.
/// </summary>
public class AccountingService(PharmaDbContext db, ITenantContext ctx, IClock clock, SequenceService sequences, AuditService audit)
{
    private static readonly (string Key, string Code, string Name, AccountType Type)[] SystemAccounts =
    [
        ("CASH", "1000", "Cash in Hand", AccountType.Asset),
        ("BANK", "1010", "Bank Account", AccountType.Asset),
        ("CLEARING", "1020", "Card / UPI Clearing", AccountType.Asset),
        ("AR", "1100", "Customer Receivables", AccountType.Asset),
        ("CLAIMS", "1150", "Supplier Claims Receivable", AccountType.Asset),
        ("INVENTORY", "1200", "Inventory", AccountType.Asset),
        ("TRANSIT", "1210", "Stock in Transit (inter-branch)", AccountType.Asset),
        ("GST_IN", "1300", "GST Input Credit", AccountType.Asset),
        ("AP", "2000", "Supplier Payables", AccountType.Liability),
        ("GST_OUT", "2100", "GST Output Payable", AccountType.Liability),
        ("CAPITAL", "3000", "Owner's Capital", AccountType.Equity),
        ("OPENING", "3100", "Opening Balance Equity", AccountType.Equity),
        ("SALES", "4000", "Sales", AccountType.Income),
        ("SALES_RETURNS", "4010", "Sales Returns", AccountType.Income),
        ("STOCK_GAIN", "4100", "Inventory Gain", AccountType.Income),
        ("ROUND_OFF", "4200", "Round Off", AccountType.Income),
        ("DELIVERY", "4300", "Delivery Charges", AccountType.Income),
        ("COGS", "5000", "Cost of Goods Sold", AccountType.Expense),
        ("STOCK_LOSS", "5100", "Stock Loss & Write-off", AccountType.Expense),
        ("CLAIM_WRITEOFF", "5110", "Supplier Claims Written Off", AccountType.Expense),
        ("FREIGHT", "5200", "Freight Inward", AccountType.Expense),
        ("LOYALTY", "5300", "Loyalty Discounts", AccountType.Expense),
        ("CASH_DIFF", "5400", "Cash Short / Over", AccountType.Expense),
        ("PETTY", "5500", "Petty Expenses", AccountType.Expense),
    ];
    private static readonly (string Code, string Name, AccountType Type)[] StarterAccounts =
    [
        ("5600", "Rent", AccountType.Expense), ("5610", "Salaries & Wages", AccountType.Expense),
        ("5620", "Electricity", AccountType.Expense), ("5630", "Bank Charges & MDR", AccountType.Expense),
        ("5690", "Other Expenses", AccountType.Expense),
    ];
    /// <summary>Sub-ledger controlled accounts: only the posting engine may touch them.</summary>
    private static readonly string[] EngineOnly = ["AR", "AP", "INVENTORY", "CLAIMS"];

    private Dictionary<string, Guid>? _keys;
    private DateOnly? _lock;

    // ===================== chart of accounts =====================

    private async Task<Dictionary<string, Guid>> KeysAsync(CancellationToken ct)
    {
        if (_keys is not null) return _keys;
        var existing = await db.LedgerAccounts.Where(a => a.SystemKey != null).ToDictionaryAsync(a => a.SystemKey!, a => a.Id, ct);
        if (existing.Count < SystemAccounts.Length)
        {
            // Serialise first-time seeding per tenant: parallel requests otherwise deadlock on the unique index (seen in browser tests).
            await using var seedTx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
            await db.Database.ExecuteSqlRawAsync(
                "EXEC sp_getapplock @Resource = {0}, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 15000",
                [$"chart:{ctx.TenantId}"], ct);
            existing = await db.LedgerAccounts.Where(a => a.SystemKey != null).ToDictionaryAsync(a => a.SystemKey!, a => a.Id, ct);
            if (existing.Count >= SystemAccounts.Length) { if (seedTx is not null) await seedTx.CommitAsync(ct); return _keys = existing; }
            var codes = await db.LedgerAccounts.Select(a => a.Code).ToListAsync(ct);
            foreach (var (key, code, name, type) in SystemAccounts.Where(s => !existing.ContainsKey(s.Key)))
            {
                var a = new LedgerAccount { Code = codes.Contains(code) ? "S" + code : code, Name = name, Type = type, SystemKey = key };
                db.LedgerAccounts.Add(a);
                existing[key] = a.Id;
            }
            foreach (var (code, name, type) in StarterAccounts.Where(s => !codes.Contains(s.Code)))
                db.LedgerAccounts.Add(new LedgerAccount { Code = code, Name = name, Type = type });
            await db.SaveChangesAsync(ct);
            if (seedTx is not null) await seedTx.CommitAsync(ct);
        }
        return _keys = existing;
    }

    public async Task<List<AccountDto>> AccountsAsync(CancellationToken ct = default)
    {
        await KeysAsync(ct);
        return await db.LedgerAccounts.AsNoTracking().OrderBy(a => a.Code)
            .Select(a => new AccountDto(a.Id, a.Code, a.Name, a.Type, a.SystemKey, a.IsActive)).ToListAsync(ct);
    }

    public async Task<AccountDto> SaveAccountAsync(Guid? id, AccountInput input, CancellationToken ct = default)
    {
        await KeysAsync(ct);
        Require(!string.IsNullOrWhiteSpace(input.Code) && input.Code.Length <= 10, "Code is required (max 10).");
        Require(!string.IsNullOrWhiteSpace(input.Name) && input.Name.Length <= 100, "Name is required (max 100).");
        Require(Enum.IsDefined(input.Type), "Invalid type.");
        if (await db.LedgerAccounts.AnyAsync(a => a.Code == input.Code.Trim() && a.Id != id, ct))
            throw BusinessException.Conflict("duplicate_code", "Another account already uses this code.");
        LedgerAccount a;
        if (id is null) { a = new LedgerAccount(); db.LedgerAccounts.Add(a); }
        else
        {
            a = await db.LedgerAccounts.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Account");
            if (a.SystemKey is not null && (a.Type != input.Type || !input.IsActive))
                throw BusinessException.Validation("System accounts can be renamed but not retyped or deactivated.");
            if (a.Type != input.Type && await db.JournalLines.AnyAsync(l => l.AccountId == a.Id, ct))
                throw BusinessException.Validation("Account type cannot change after it has entries.");
        }
        a.Code = input.Code.Trim(); a.Name = input.Name.Trim(); a.Type = input.Type; a.IsActive = input.IsActive;
        audit.Record("account.saved", nameof(LedgerAccount), a.Id, $"{a.Code} {a.Name}");
        await db.SaveChangesAsync(ct);
        return new AccountDto(a.Id, a.Code, a.Name, a.Type, a.SystemKey, a.IsActive);
    }

    // ===================== posting engine =====================

    private sealed class Voucher(Guid branchId, DateOnly date, string sourceType, Guid sourceId, string evt, string? sourceNo, string narration)
    {
        public Guid BranchId = branchId; public DateOnly Date = date; public string SourceType = sourceType; public Guid SourceId = sourceId;
        public string Event = evt; public string? SourceNo = sourceNo; public string Narration = narration; public Guid? Reverses;
        public readonly List<(Guid Account, decimal Dr, decimal Cr, Guid? Party)> Lines = [];
    }

    private void Dr(Voucher v, string key, decimal amount, Guid? party = null) => Add(v, _keys![key], amount, party, debit: true);
    private void Cr(Voucher v, string key, decimal amount, Guid? party = null) => Add(v, _keys![key], amount, party, debit: false);
    private static void Add(Voucher v, Guid account, decimal amount, Guid? party, bool debit)
    {
        amount = Money.R2(amount);
        if (amount == 0) return;
        if (amount < 0) { amount = -amount; debit = !debit; }
        v.Lines.Add((account, debit ? amount : 0, debit ? 0 : amount, party));
    }

    private static string PaymentAccount(PaymentMode m) => m switch
    {
        PaymentMode.Cash => "CASH", PaymentMode.Upi or PaymentMode.Card => "CLEARING", PaymentMode.Credit => "AR", PaymentMode.Loyalty => "LOYALTY",
        _ => throw new InvalidOperationException($"No account for {m}"),
    };
    private static string SettlementAccount(SettlementMode m) => m switch
    {
        SettlementMode.Cash => "CASH", SettlementMode.Upi or SettlementMode.Card => "CLEARING", _ => "BANK",
    };
    private DateOnly Local(DateTimeOffset at) => BusinessCalendar.LocalDate(at);

    private async Task<int> SaveAsync(Voucher v, CancellationToken ct)
    {
        var dr = v.Lines.Sum(l => l.Dr);
        var cr = v.Lines.Sum(l => l.Cr);
        if (dr != cr) throw new InvalidOperationException($"Unbalanced journal for {v.SourceType} {v.SourceNo} {v.Event}: Dr {dr} ≠ Cr {cr}");
        if (v.Lines.Count == 0) return 0;
        if (_lock is { } l && v.Date <= l) { v.Narration += $" (event dated {v.Date:dd-MMM-yyyy}, books closed)"; v.Date = l.AddDays(1); }
        // own transaction: holds the voucher-number row lock until the entry is inserted
        var ownTx = db.Database.CurrentTransaction is null ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            await InsertAsync(v, ct);
            if (ownTx is not null) await ownTx.CommitAsync(ct);
            return 1;
        }
        catch (DbUpdateException) when (ownTx is not null)
        {
            // another request posted this same document event first (unique index) — nothing to do
            await ownTx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var exists = await db.JournalEntries.AnyAsync(j => j.SourceType == v.SourceType && j.SourceId == v.SourceId && j.Event == v.Event, ct);
            if (!exists) throw;
            return 0;
        }
        finally
        {
            if (ownTx is not null) await ownTx.DisposeAsync();
        }
    }

    private async Task InsertAsync(Voucher v, CancellationToken ct)
    {
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == v.BranchId, ct);
        var e = new JournalEntry
        {
            BranchId = v.BranchId, Date = v.Date, SourceType = v.SourceType, SourceId = v.SourceId, Event = v.Event, SourceNo = v.SourceNo,
            Narration = v.Narration.Length > 500 ? v.Narration[..500] : v.Narration, ReversesEntryId = v.Reverses, CreatedAt = clock.UtcNow,
            CreatedByUserId = ctx.UserId == Guid.Empty ? null : ctx.UserId,
            EntryNo = SequenceService.Format(branch.Code, "J", v.Date, await sequences.NextAsync(branch.Id, "J", v.Date, ct)),
        };
        e.Lines = v.Lines.Select(x => new JournalLine { JournalEntryId = e.Id, AccountId = x.Account, Debit = x.Dr, Credit = x.Cr, PartyId = x.Party }).ToList();
        db.JournalEntries.Add(e);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Reverse an earlier entry (swap debits and credits) as a new event of the same source.</summary>
    private async Task<int> ReverseAsync(string sourceType, Guid sourceId, string originalEvent, string evt, DateOnly date, string narration, CancellationToken ct)
    {
        var original = await db.JournalEntries.AsNoTracking().Include(j => j.Lines)
            .SingleOrDefaultAsync(j => j.SourceType == sourceType && j.SourceId == sourceId && j.Event == originalEvent, ct);
        if (original is null) return 0;
        var v = new Voucher(original.BranchId, date, sourceType, sourceId, evt, original.SourceNo, narration) { Reverses = original.Id };
        foreach (var l in original.Lines) v.Lines.Add((l.AccountId, l.Credit, l.Debit, l.PartyId));
        return await SaveAsync(v, ct);
    }

    private IQueryable<T> Missing<T>(IQueryable<T> q, string sourceType, string evt) where T : Entity =>
        q.Where(d => !db.JournalEntries.Any(j => j.SourceType == sourceType && j.SourceId == d.Id && j.Event == evt));

    /// <summary>
    /// Brings the ledger up to date with every business document. Idempotent: each document event is journaled once
    /// (enforced by a unique index), so it is safe to call repeatedly — accounting screens call it before reporting.
    /// </summary>
    public async Task<SyncResult> SyncAsync(CancellationToken ct = default)
    {
        await KeysAsync(ct);
        _lock = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct)).BooksLockedUntil;
        var n = 0;
        const int batch = 300;

        // --- purchases ---
        foreach (var p in await Missing(db.PurchaseInvoices.Where(p => p.PostedAt != null), nameof(PurchaseInvoice), "post")
                     .Include(p => p.Lines).Include(p => p.Supplier).AsNoTracking().OrderBy(p => p.PostedAt).Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(p.BranchId, Local(p.PostedAt!.Value), nameof(PurchaseInvoice), p.Id, "post", p.SupplierInvoiceNo, $"Purchase {p.SupplierInvoiceNo} from {p.Supplier!.Name}");
            Dr(v, "INVENTORY", p.Lines.Sum(l => l.TaxableAmount));
            Dr(v, "GST_IN", p.Lines.Sum(l => l.TaxAmount));
            Dr(v, "FREIGHT", p.OtherCharges);
            Dr(v, "ROUND_OFF", p.RoundOff);
            Cr(v, "AP", p.NetAmount, p.SupplierId);
            n += await SaveAsync(v, ct);
        }
        foreach (var p in await Missing(db.PurchaseInvoices.Where(p => p.Status == PurchaseStatus.Cancelled && p.PostedAt != null), nameof(PurchaseInvoice), "cancel")
                     .AsNoTracking().Take(batch).ToListAsync(ct))
            n += await ReverseAsync(nameof(PurchaseInvoice), p.Id, "post", "cancel", Local(p.CancelledAt ?? clock.UtcNow), $"Cancelled purchase {p.SupplierInvoiceNo}: {p.CancelReason}", ct);

        // --- sales ---
        foreach (var s in await Missing(db.SalesInvoices, nameof(SalesInvoice), "post").Include(s => s.Lines).Include(s => s.Payments)
                     .AsSplitQuery().AsNoTracking().OrderBy(s => s.InvoiceAt).Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(s.BranchId, s.BusinessDate, nameof(SalesInvoice), s.Id, "post", s.InvoiceNo, $"Sale {s.InvoiceNo}{(s.CustomerName is null ? "" : " to " + s.CustomerName)}");
            foreach (var pay in s.Payments) Dr(v, PaymentAccount(pay.Mode), pay.Amount, pay.Mode == PaymentMode.Credit ? s.CustomerId : null);
            Cr(v, "SALES", s.Lines.Sum(l => l.TaxableAmount));
            Cr(v, "DELIVERY", s.DeliveryChargeTaxable);
            Cr(v, "GST_OUT", s.Lines.Sum(l => l.TaxAmount) + s.DeliveryChargeTax);
            Cr(v, "ROUND_OFF", s.RoundOff);
            var cost = s.Lines.Sum(l => Money.R2(l.QtyUnits * l.CostPerUnit));
            Dr(v, "COGS", cost);
            Cr(v, "INVENTORY", cost);
            n += await SaveAsync(v, ct);
        }
        foreach (var s in await Missing(db.SalesInvoices.Where(s => s.Status == SalesStatus.Cancelled), nameof(SalesInvoice), "cancel").AsNoTracking().Take(batch).ToListAsync(ct))
            n += await ReverseAsync(nameof(SalesInvoice), s.Id, "post", "cancel", Local(s.CancelledAt ?? clock.UtcNow), $"Cancelled bill {s.InvoiceNo}: {s.CancelReason}", ct);

        // --- sales returns ---
        foreach (var r in await Missing(db.SalesReturns, nameof(SalesReturn), "post").Include(r => r.Lines).AsNoTracking().OrderBy(r => r.ReturnAt).Take(batch).ToListAsync(ct))
        {
            var lineIds = r.Lines.Select(l => l.SalesInvoiceLineId).ToList();
            var orig = await db.SalesInvoiceLines.AsNoTracking().Where(l => lineIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
            var inv = await db.SalesInvoices.AsNoTracking().Where(i => i.Id == r.SalesInvoiceId).Select(i => new { i.InvoiceNo, i.CustomerId }).SingleAsync(ct);
            var v = new Voucher(r.BranchId, r.BusinessDate, nameof(SalesReturn), r.Id, "post", r.ReturnNo, $"Return {r.ReturnNo} against {inv.InvoiceNo}: {r.Reason}");
            var split = r.Lines.Select(l => Money.SplitInclusive(l.Amount, orig[l.SalesInvoiceLineId].GstRate)).ToList();
            Dr(v, "SALES_RETURNS", split.Sum(x => x.Taxable));
            Dr(v, "GST_OUT", split.Sum(x => x.Tax));
            Cr(v, PaymentAccount(r.RefundMode), r.RefundAmount, r.RefundMode == PaymentMode.Credit ? inv.CustomerId : null);
            var cost = r.Lines.Sum(l => Money.R2(l.QtyUnits * orig[l.SalesInvoiceLineId].CostPerUnit));
            Dr(v, "INVENTORY", cost);
            Cr(v, "COGS", cost);
            n += await SaveAsync(v, ct);
        }

        // --- customer receipts & supplier payments ---
        foreach (var r in await Missing(db.CustomerReceipts, nameof(CustomerReceipt), "post").Include(r => r.Customer).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(r.BranchId, r.ReceiptDate, nameof(CustomerReceipt), r.Id, "post", r.ReceiptNo, $"Receipt {r.ReceiptNo} from {r.Customer!.Name} ({r.Mode})");
            Dr(v, SettlementAccount(r.Mode), r.Amount);
            Cr(v, "AR", r.Amount, r.CustomerId);
            n += await SaveAsync(v, ct);
        }
        foreach (var r in await Missing(db.CustomerReceipts.Where(r => r.Status == DocStatus.Cancelled), nameof(CustomerReceipt), "cancel").AsNoTracking().Take(batch).ToListAsync(ct))
            n += await ReverseAsync(nameof(CustomerReceipt), r.Id, "post", "cancel", Local(clock.UtcNow), $"Cancelled receipt {r.ReceiptNo}: {r.CancelReason}", ct);
        foreach (var p in await Missing(db.SupplierPayments, nameof(SupplierPayment), "post").Include(p => p.Supplier).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(p.BranchId, p.PaymentDate, nameof(SupplierPayment), p.Id, "post", p.PaymentNo, $"Payment {p.PaymentNo} to {p.Supplier!.Name} ({p.Mode} {p.Reference})");
            Dr(v, "AP", p.Amount, p.SupplierId);
            Cr(v, SettlementAccount(p.Mode), p.Amount);
            n += await SaveAsync(v, ct);
        }
        foreach (var p in await Missing(db.SupplierPayments.Where(p => p.Status == DocStatus.Cancelled), nameof(SupplierPayment), "cancel").AsNoTracking().Take(batch).ToListAsync(ct))
            n += await ReverseAsync(nameof(SupplierPayment), p.Id, "post", "cancel", Local(clock.UtcNow), $"Cancelled payment {p.PaymentNo}: {p.CancelReason}", ct);

        // --- returns to supplier ---
        foreach (var r in await Missing(db.PurchaseReturns.Where(r => r.DispatchedAt != null), nameof(PurchaseReturn), "dispatch")
                     .Include(r => r.Lines).Include(r => r.Supplier).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(r.BranchId, Local(r.DispatchedAt!.Value), nameof(PurchaseReturn), r.Id, "dispatch", r.ReturnNo, $"Goods returned to {r.Supplier!.Name} ({r.Reason})");
            var cost = r.Lines.Sum(l => Money.R2(l.QtyUnits * l.CostPerUnit));
            Dr(v, "CLAIMS", r.ClaimAmount, r.SupplierId);
            Cr(v, "INVENTORY", cost);
            Cr(v, "GST_IN", r.ClaimAmount - cost);
            n += await SaveAsync(v, ct);
        }
        foreach (var r in await Missing(db.PurchaseReturns.Where(r => r.Status == PurchaseReturnStatus.Settled), nameof(PurchaseReturn), "settle")
                     .Include(r => r.Supplier).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(r.BranchId, Local(r.SettledAt ?? clock.UtcNow), nameof(PurchaseReturn), r.Id, "settle", r.ReturnNo,
                $"Credit note {r.SupplierCreditNoteNo} from {r.Supplier!.Name} for {r.ReturnNo}");
            Dr(v, "AP", r.AcceptedAmount, r.SupplierId);
            Dr(v, "CLAIM_WRITEOFF", r.WrittenOffAmount);
            Cr(v, "CLAIMS", r.ClaimAmount, r.SupplierId);
            n += await SaveAsync(v, ct);
        }
        foreach (var r in await Missing(db.PurchaseReturns.Where(r => r.Status == PurchaseReturnStatus.Rejected), nameof(PurchaseReturn), "reject").AsNoTracking().Take(batch).ToListAsync(ct))
            n += await ReverseAsync(nameof(PurchaseReturn), r.Id, "dispatch", "reject", Local(r.SettledAt ?? clock.UtcNow), $"Supplier rejected {r.ReturnNo}: {r.RejectReason}", ct);

        // --- stock adjustments & counts ---
        foreach (var a in await Missing(db.StockAdjustments, nameof(StockAdjustment), "post").Include(a => a.Lines).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var batchIds = a.Lines.Select(l => l.BatchId).ToList();
            var costs = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.CostPerUnit, ct);
            var v = new Voucher(a.BranchId, Local(a.CreatedAt), nameof(StockAdjustment), a.Id, "post", a.AdjustmentNo, $"{(a.IsOpening ? "Opening stock" : "Stock adjustment")} {a.AdjustmentNo}: {a.Reason}");
            foreach (var l in a.Lines)
            {
                var value = Money.R2(Math.Abs(l.QtyUnits) * costs[l.BatchId]);
                if (l.QtyUnits > 0) { Dr(v, "INVENTORY", value); Cr(v, a.IsOpening ? "OPENING" : "STOCK_GAIN", value); }
                else { Dr(v, "STOCK_LOSS", value); Cr(v, "INVENTORY", value); }
            }
            n += await SaveAsync(v, ct);
        }
        foreach (var c in await Missing(db.StockCounts.Where(c => c.Status == StockCountStatus.Posted), nameof(StockCount), "post").Include(c => c.Lines).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(c.BranchId, Local(c.PostedAt!.Value), nameof(StockCount), c.Id, "post", c.CountNo, $"Stock count {c.CountNo} {c.Title}");
            foreach (var l in c.Lines.Where(l => l.CountedQty is not null && l.CountedQty != l.SystemQtyAtCount))
            {
                var diff = l.CountedQty!.Value - l.SystemQtyAtCount!.Value;
                var value = Money.R2(Math.Abs(diff) * l.CostPerUnit);
                if (diff > 0) { Dr(v, "INVENTORY", value); Cr(v, "STOCK_GAIN", value); }
                else { Dr(v, "STOCK_LOSS", value); Cr(v, "INVENTORY", value); }
            }
            n += await SaveAsync(v, ct);
        }

        // --- inter-branch transfers (each branch's books stay balanced; Stock in Transit nets to zero across branches) ---
        foreach (var t in await Missing(db.StockTransfers.Where(t => t.DispatchedAt != null), nameof(StockTransfer), "dispatch").Include(t => t.Lines).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(t.FromBranchId, Local(t.DispatchedAt!.Value), nameof(StockTransfer), t.Id, "dispatch", t.TransferNo, $"Stock sent out on transfer {t.TransferNo}");
            var value = t.Lines.Sum(l => Money.R2(l.QtyUnits * l.CostPerUnit));
            Dr(v, "TRANSIT", value);
            Cr(v, "INVENTORY", value);
            n += await SaveAsync(v, ct);
        }
        foreach (var t in await Missing(db.StockTransfers.Where(t => t.Status == TransferStatus.Received), nameof(StockTransfer), "receive").Include(t => t.Lines).AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(t.ToBranchId, Local(t.ReceivedAt!.Value), nameof(StockTransfer), t.Id, "receive", t.TransferNo, $"Stock received on transfer {t.TransferNo}");
            var sent = t.Lines.Sum(l => Money.R2(l.QtyUnits * l.CostPerUnit));
            var received = t.Lines.Sum(l => Money.R2((l.ReceivedQtyUnits ?? 0) * l.CostPerUnit));
            Dr(v, "INVENTORY", received);
            Dr(v, "STOCK_LOSS", sent - received);
            Cr(v, "TRANSIT", sent);
            n += await SaveAsync(v, ct);
        }

        // --- cash drawer ---
        foreach (var m in await Missing(db.CashMovements, nameof(CashMovement), "post").AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var branchId = await db.CashShifts.Where(s => s.Id == m.CashShiftId).Select(s => s.BranchId).SingleAsync(ct);
            var v = new Voucher(branchId, Local(m.At), nameof(CashMovement), m.Id, "post", null, $"Petty cash {m.Kind}: {m.Reason}");
            if (m.Kind == CashMovementKind.PayOut) { Dr(v, "PETTY", m.Amount); Cr(v, "CASH", m.Amount); }
            else { Dr(v, "CASH", m.Amount); Cr(v, "BANK", m.Amount); }
            n += await SaveAsync(v, ct);
        }
        foreach (var s in await Missing(db.CashShifts.Where(s => s.Status == ShiftStatus.Closed && s.Variance != 0), nameof(CashShift), "close").AsNoTracking().Take(batch).ToListAsync(ct))
        {
            var v = new Voucher(s.BranchId, Local(s.ClosedAt!.Value), nameof(CashShift), s.Id, "close", s.ShiftNo, $"Cash {(s.Variance > 0 ? "over" : "short")} at close of {s.ShiftNo}");
            Dr(v, "CASH", s.Variance!.Value);
            Cr(v, "CASH_DIFF", s.Variance.Value);
            n += await SaveAsync(v, ct);
        }

        // keep going until nothing is left (large backlogs are processed in batches)
        if (n >= batch) n += (await SyncAsync(ct)).EntriesCreated;
        return new SyncResult(n, clock.UtcNow);
    }

    // ===================== manual journals =====================

    public async Task<JournalDto> CreateManualAsync(ManualJournalInput input, CancellationToken ct = default)
    {
        var keys = await KeysAsync(ct);
        Require(!string.IsNullOrWhiteSpace(input.Narration), "Narration is required.");
        Require(input.Lines is { Count: >= 2 and <= 50 }, "A journal needs 2-50 lines.");
        Require(input.Date <= BusinessCalendar.LocalDate(clock.UtcNow), "Journal date cannot be in the future.");
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        if (tenant.BooksLockedUntil is { } l && input.Date <= l)
            throw BusinessException.Conflict("period_locked", $"Books are closed up to {l:dd-MMM-yyyy}.");
        var ids = input.Lines.Select(x => x.AccountId).Distinct().ToList();
        var accounts = await db.LedgerAccounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var line in input.Lines)
        {
            if (!accounts.TryGetValue(line.AccountId, out var a) || !a.IsActive) throw BusinessException.Validation("Unknown or inactive account.");
            if (a.SystemKey is not null && EngineOnly.Contains(a.SystemKey))
                throw BusinessException.Validation($"{a.Name} is maintained automatically from documents and can't be used in a manual journal.");
            Require(line.Debit >= 0 && line.Credit >= 0 && (line.Debit == 0) != (line.Credit == 0), "Each line needs either a debit or a credit.");
        }
        var v = new Voucher(ctx.BranchId, input.Date, "Manual", Guid.CreateVersion7(), "manual", null, input.Narration.Trim());
        foreach (var line in input.Lines) Add(v, line.AccountId, line.Debit > 0 ? line.Debit : line.Credit, null, debit: line.Debit > 0);
        if (v.Lines.Sum(x => x.Dr) != v.Lines.Sum(x => x.Cr)) throw BusinessException.Validation("Debits must equal credits.");
        await SaveAsync(v, ct);
        audit.Record("journal.manual", nameof(JournalEntry), v.SourceId, input.Narration);
        await db.SaveChangesAsync(ct);
        return (await JournalsAsync(null, null, null, v.SourceId, ct)).Single();
    }

    public async Task<JournalDto> ReverseManualAsync(Guid entryId, CancellationToken ct = default)
    {
        await KeysAsync(ct);
        var e = await db.JournalEntries.AsNoTracking().SingleOrDefaultAsync(j => j.Id == entryId, ct) ?? throw BusinessException.NotFound("Journal");
        if (e.SourceType != "Manual") throw BusinessException.Validation("Only manual journals can be reversed here; documents are reversed by cancelling them.");
        if (await db.JournalEntries.AnyAsync(j => j.ReversesEntryId == e.Id, ct)) throw BusinessException.Conflict("already_reversed", "This journal is already reversed.");
        _lock = (await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct)).BooksLockedUntil;
        await ReverseAsync("Manual", e.SourceId, "manual", "reverse", BusinessCalendar.LocalDate(clock.UtcNow), $"Reversal of {e.EntryNo}: {e.Narration}", ct);
        audit.Record("journal.reversed", nameof(JournalEntry), e.Id, e.EntryNo);
        await db.SaveChangesAsync(ct);
        return (await JournalsAsync(null, null, null, e.SourceId, ct)).Single(j => j.Event == "reverse");
    }

    public async Task<List<JournalDto>> JournalsAsync(DateOnly? from, DateOnly? to, string? sourceType, Guid? sourceId = null, CancellationToken ct = default)
    {
        var q = db.JournalEntries.AsNoTracking().Where(j => j.BranchId == ctx.BranchId);
        if (from is not null) q = q.Where(j => j.Date >= from);
        if (to is not null) q = q.Where(j => j.Date <= to);
        if (!string.IsNullOrWhiteSpace(sourceType)) q = q.Where(j => j.SourceType == sourceType);
        if (sourceId is not null) q = q.Where(j => j.SourceId == sourceId);
        var rows = await q.OrderByDescending(j => j.Date).ThenByDescending(j => j.CreatedAt).Take(500)
            .Include(j => j.Lines).ThenInclude(l => l.Account).AsSplitQuery().ToListAsync(ct);
        return rows.Select(j => new JournalDto(j.Id, j.EntryNo, j.Date, j.SourceType, j.Event, j.SourceNo, j.SourceId, j.Narration, j.ReversesEntryId,
            j.Lines.Sum(l => l.Debit), j.Lines.OrderByDescending(l => l.Debit).Select(l => new JournalLineDto(l.AccountId, l.Account!.Code, l.Account.Name, l.Debit, l.Credit, l.PartyId)).ToList())).ToList();
    }

    // ===================== reports =====================

    private async Task<List<(LedgerAccount A, decimal Dr, decimal Cr)>> TotalsAsync(DateOnly? from, DateOnly to, CancellationToken ct)
    {
        await SyncAsync(ct);
        var lines = db.JournalLines.AsNoTracking().Where(l => db.JournalEntries.Any(j => j.Id == l.JournalEntryId && j.BranchId == ctx.BranchId
                                                                                      && j.Date <= to && (from == null || j.Date >= from)));
        var sums = await lines.GroupBy(l => l.AccountId).Select(g => new { g.Key, Dr = g.Sum(x => x.Debit), Cr = g.Sum(x => x.Credit) }).ToDictionaryAsync(x => x.Key, ct);
        var accounts = await db.LedgerAccounts.AsNoTracking().OrderBy(a => a.Code).ToListAsync(ct);
        return accounts.Select(a => sums.TryGetValue(a.Id, out var s) ? (a, s.Dr, s.Cr) : (a, 0m, 0m)).ToList();
    }

    private static decimal Natural(AccountType t, decimal dr, decimal cr) => t is AccountType.Asset or AccountType.Expense ? dr - cr : cr - dr;

    public async Task<TrialBalance> TrialBalanceAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var rows = (await TotalsAsync(null, asOf, ct)).Where(x => x.Dr != 0 || x.Cr != 0)
            .Select(x => new TrialBalanceRow(x.A.Id, x.A.Code, x.A.Name, x.A.Type, x.Dr > x.Cr ? x.Dr - x.Cr : 0, x.Cr > x.Dr ? x.Cr - x.Dr : 0, Natural(x.A.Type, x.Dr, x.Cr)))
            .ToList();
        var dr = rows.Sum(r => r.Debit);
        var cr = rows.Sum(r => r.Credit);
        return new TrialBalance(asOf, rows, dr, cr, dr == cr);
    }

    public async Task<ProfitAndLoss> ProfitAndLossAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(from <= to, "From must be before To.");
        var t = await TotalsAsync(from, to, ct);
        var income = t.Where(x => x.A.Type == AccountType.Income && (x.Dr != 0 || x.Cr != 0)).Select(x => new StatementRow(x.A.Code, x.A.Name, x.Cr - x.Dr)).ToList();
        var exp = t.Where(x => x.A.Type == AccountType.Expense && (x.Dr != 0 || x.Cr != 0)).Select(x => new StatementRow(x.A.Code, x.A.Name, x.Dr - x.Cr)).ToList();
        return new ProfitAndLoss(from, to, income, income.Sum(x => x.Amount), exp, exp.Sum(x => x.Amount), income.Sum(x => x.Amount) - exp.Sum(x => x.Amount));
    }

    public async Task<BalanceSheet> BalanceSheetAsync(DateOnly asOf, CancellationToken ct = default)
    {
        var t = await TotalsAsync(null, asOf, ct);
        List<StatementRow> Of(AccountType type) => t.Where(x => x.A.Type == type && (x.Dr != 0 || x.Cr != 0))
            .Select(x => new StatementRow(x.A.Code, x.A.Name, Natural(type, x.Dr, x.Cr))).ToList();
        var assets = Of(AccountType.Asset);
        var liabilities = Of(AccountType.Liability);
        var equity = Of(AccountType.Equity);
        var earnings = t.Where(x => x.A.Type == AccountType.Income).Sum(x => x.Cr - x.Dr) - t.Where(x => x.A.Type == AccountType.Expense).Sum(x => x.Dr - x.Cr);
        var totalEquity = equity.Sum(x => x.Amount) + earnings;
        return new BalanceSheet(asOf, assets, assets.Sum(x => x.Amount), liabilities, liabilities.Sum(x => x.Amount), equity, earnings, totalEquity,
            assets.Sum(x => x.Amount) == liabilities.Sum(x => x.Amount) + totalEquity);
    }

    public async Task<AccountLedger> LedgerAsync(Guid accountId, DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await SyncAsync(ct);
        var a = await db.LedgerAccounts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == accountId, ct) ?? throw BusinessException.NotFound("Account");
        var lines = db.JournalLines.AsNoTracking().Where(l => l.AccountId == accountId)
            .Join(db.JournalEntries.Where(j => j.BranchId == ctx.BranchId), l => l.JournalEntryId, j => j.Id, (l, j) => new { l.Debit, l.Credit, j.Date, j.EntryNo, j.SourceType, j.SourceNo, j.Narration, j.CreatedAt });
        var openingSums = await lines.Where(x => x.Date < from).GroupBy(_ => 1).Select(g => new { Dr = g.Sum(x => x.Debit), Cr = g.Sum(x => x.Credit) }).SingleOrDefaultAsync(ct);
        var opening = openingSums is null ? 0 : Natural(a.Type, openingSums.Dr, openingSums.Cr);
        var period = await lines.Where(x => x.Date >= from && x.Date <= to).OrderBy(x => x.Date).ThenBy(x => x.CreatedAt).Take(5000).ToListAsync(ct);
        var bal = opening;
        var rows = period.Select(x => { bal += Natural(a.Type, x.Debit, x.Credit); return new LedgerRow(x.Date, x.EntryNo, x.SourceType, x.SourceNo, x.Narration, x.Debit, x.Credit, bal); }).ToList();
        return new AccountLedger(a.Id, a.Code, a.Name, from, to, opening, rows, bal);
    }

    public async Task<Guid> AccountIdAsync(string systemKey, CancellationToken ct = default) => (await KeysAsync(ct))[systemKey];

    /// <summary>
    /// Informational GST summary from documents (NOT a return / filing; place of supply, exemptions, ITC eligibility
    /// and reverse charge are not evaluated). Outward = sales − sales returns by business date; inward = posted purchases by posting date.
    /// </summary>
    public async Task<GstSummary> GstSummaryAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(from <= to, "From must be before To.");
        var b = ctx.BranchId;
        var sales = await db.SalesInvoiceLines.AsNoTracking()
            .Where(l => db.SalesInvoices.Any(s => s.Id == l.SalesInvoiceId && s.BranchId == b && s.Status == SalesStatus.Posted && s.BusinessDate >= from && s.BusinessDate <= to))
            .GroupBy(l => l.GstRate).Select(g => new { Rate = g.Key, Taxable = g.Sum(x => x.TaxableAmount), Tax = g.Sum(x => x.TaxAmount) }).ToListAsync(ct);
        var returns = await db.SalesReturnLines.AsNoTracking()
            .Where(l => db.SalesReturns.Any(r => r.Id == l.SalesReturnId && r.BranchId == b && r.BusinessDate >= from && r.BusinessDate <= to))
            .Join(db.SalesInvoiceLines, l => l.SalesInvoiceLineId, il => il.Id, (l, il) => new { l.Amount, il.GstRate }).ToListAsync(ct);
        var deliveries = await db.SalesInvoices.AsNoTracking()
            .Where(s => s.BranchId == b && s.Status == SalesStatus.Posted && s.BusinessDate >= from && s.BusinessDate <= to && s.DeliveryCharge > 0)
            .GroupBy(s => s.DeliveryChargeGstRate).Select(g => new { Rate = g.Key, Taxable = g.Sum(x => x.DeliveryChargeTaxable), Tax = g.Sum(x => x.DeliveryChargeTax) }).ToListAsync(ct);
        var outward = sales.Select(s => s.Rate).Union(returns.Select(r => r.GstRate)).Union(deliveries.Select(d => d.Rate)).Distinct().OrderBy(r => r).Select(rate =>
        {
            var ret = returns.Where(r => r.GstRate == rate).Select(r => Money.SplitInclusive(r.Amount, rate)).ToList();
            var s = sales.SingleOrDefault(x => x.Rate == rate);
            var d = deliveries.SingleOrDefault(x => x.Rate == rate);
            return new GstRateRow(rate, (s?.Taxable ?? 0) + (d?.Taxable ?? 0) - ret.Sum(x => x.Taxable), (s?.Tax ?? 0) + (d?.Tax ?? 0) - ret.Sum(x => x.Tax));
        }).ToList();

        var fromUtc = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.FromHours(5.5));
        var toUtc = new DateTimeOffset(to.ToDateTime(TimeOnly.MaxValue), TimeSpan.FromHours(5.5));
        var inward = await db.PurchaseInvoiceLines.AsNoTracking()
            .Where(l => db.PurchaseInvoices.Any(p => p.Id == l.PurchaseInvoiceId && p.BranchId == b && p.Status == PurchaseStatus.Posted && p.PostedAt >= fromUtc && p.PostedAt <= toUtc))
            .GroupBy(l => l.GstRate).Select(g => new GstRateRow(g.Key, g.Sum(x => x.TaxableAmount), g.Sum(x => x.TaxAmount))).ToListAsync(ct);
        var prTax = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.BranchId == b && r.DispatchedAt >= fromUtc && r.DispatchedAt <= toUtc && r.Status != PurchaseReturnStatus.Rejected)
            .SelectMany(r => r.Lines).ToListAsync(ct);
        var returnsTax = prTax.Sum(l => l.ClaimAmount - Money.R2(l.QtyUnits * l.CostPerUnit));
        var output = outward.Sum(x => x.Tax);
        var input = inward.Sum(x => x.Tax) - returnsTax;
        return new GstSummary(from, to, outward, output, inward.OrderBy(x => x.Rate).ToList(), inward.Sum(x => x.Tax), returnsTax, output - input);
    }

    /// <summary>Control-account checks: ledger balances must equal the operational sub-ledgers.</summary>
    public async Task<List<ControlCheck>> ControlChecksAsync(LedgerService ledger, CancellationToken ct = default)
    {
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var tb = (await TrialBalanceAsync(today, ct)).Rows;
        decimal Bal(string key) => tb.SingleOrDefault(r => r.AccountId == _keys![key])?.Balance ?? 0;
        var receivables = (await ledger.ReceivablesSummaryAsync(ct)).Sum(x => x.Balance);
        var payables = (await ledger.PayablesSummaryAsync(ct)).Sum(x => x.Balance);
        var pendingClaims = await db.PurchaseReturns.Where(r => r.BranchId == ctx.BranchId && r.Status == PurchaseReturnStatus.Dispatched).SumAsync(r => (decimal?)r.ClaimAmount, ct) ?? 0;
        var stockValue = await db.StockBalances.Where(s => s.BranchId == ctx.BranchId).SumAsync(s => (decimal?)(s.QtyUnits * s.Batch!.CostPerUnit), ct) ?? 0;
        return
        [
            new("Customer receivables", Bal("AR"), receivables, Bal("AR") - receivables, "Ledger vs customer dues"),
            new("Supplier payables", Bal("AP"), payables, Bal("AP") - payables, "Ledger vs supplier outstanding"),
            new("Supplier claims", Bal("CLAIMS"), pendingClaims, Bal("CLAIMS") - pendingClaims, "Ledger vs dispatched returns awaiting credit"),
            new("Inventory", Bal("INVENTORY"), Money.R2(stockValue), Bal("INVENTORY") - Money.R2(stockValue),
                "Ledger (cost at time of movement) vs stock × current batch cost; small differences come from rounding or re-costed batches"),
        ];
    }

    // ===================== period close & export =====================

    public async Task<DateOnly?> CloseAsync(CloseRequest r, CancellationToken ct = default)
    {
        Require(r.UpTo < BusinessCalendar.LocalDate(clock.UtcNow), "You can only close periods that have ended.");
        await SyncAsync(ct);
        var tb = await TrialBalanceAsync(r.UpTo, ct);
        if (!tb.Balanced) throw BusinessException.Conflict("unbalanced", "Trial balance does not balance; contact support before closing.");
        var t = await db.Tenants.SingleAsync(x => x.Id == ctx.TenantId, ct);
        if (t.BooksLockedUntil is { } l && r.UpTo <= l) throw BusinessException.Validation($"Books are already closed up to {l:dd-MMM-yyyy}.");
        t.BooksLockedUntil = r.UpTo;
        audit.Record("books.closed", nameof(Tenant), t.Id, $"up to {r.UpTo:yyyy-MM-dd}");
        await db.SaveChangesAsync(ct);
        return t.BooksLockedUntil;
    }

    public async Task<DateOnly?> ReopenAsync(DateOnly? upTo, CancellationToken ct = default)
    {
        var t = await db.Tenants.SingleAsync(x => x.Id == ctx.TenantId, ct);
        Require(t.BooksLockedUntil is not null, "Books are not closed.");
        Require(upTo is null || upTo < t.BooksLockedUntil, "New lock date must be earlier than the current one.");
        audit.Record("books.reopened", nameof(Tenant), t.Id, $"from {t.BooksLockedUntil:yyyy-MM-dd} to {(upTo is null ? "open" : upTo.Value.ToString("yyyy-MM-dd"))}");
        t.BooksLockedUntil = upTo;
        await db.SaveChangesAsync(ct);
        return t.BooksLockedUntil;
    }

    public async Task<DateOnly?> LockDateAsync(CancellationToken ct = default) =>
        (await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct)).BooksLockedUntil;

    /// <summary>
    /// Tally-style XML (ledger masters + journal vouchers) for import by the pharmacy's accountant.
    /// NOT validated against a live Tally installation; the ledger → Tally group mapping below is a default for the CA to review.
    /// Tally convention: debit lines carry ISDEEMEDPOSITIVE=Yes and a negative AMOUNT; credits ISDEEMEDPOSITIVE=No and a positive AMOUNT.
    /// </summary>
    public async Task<string> ExportTallyXmlAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(from <= to, "From must be before To.");
        await SyncAsync(ct);
        var accounts = await db.LedgerAccounts.AsNoTracking().ToListAsync(ct);
        var entries = await db.JournalEntries.AsNoTracking().Where(j => j.BranchId == ctx.BranchId && j.Date >= from && j.Date <= to)
            .Include(j => j.Lines).OrderBy(j => j.Date).ThenBy(j => j.CreatedAt).AsSplitQuery().ToListAsync(ct);
        var used = entries.SelectMany(e => e.Lines).Select(l => l.AccountId).ToHashSet();
        string Name(Guid id) { var a = accounts.First(x => x.Id == id); return $"{a.Name} ({a.Code})"; }
        static string X(string s) => System.Security.SecurityElement.Escape(s) ?? "";
        static string Group(LedgerAccount a) => a.SystemKey switch
        {
            "CASH" => "Cash-in-Hand", "BANK" or "CLEARING" => "Bank Accounts", "AR" => "Sundry Debtors", "AP" => "Sundry Creditors",
            "GST_IN" or "GST_OUT" => "Duties & Taxes", "INVENTORY" or "TRANSIT" or "CLAIMS" => "Current Assets",
            "SALES" or "SALES_RETURNS" => "Sales Accounts", "COGS" => "Purchase Accounts", "CAPITAL" or "OPENING" => "Capital Account",
            _ => a.Type switch
            {
                AccountType.Asset => "Current Assets", AccountType.Liability => "Current Liabilities", AccountType.Equity => "Capital Account",
                AccountType.Income => "Indirect Incomes", _ => "Indirect Expenses",
            },
        };

        var sb = new StringBuilder();
        sb.Append("<ENVELOPE><HEADER><TALLYREQUEST>Import Data</TALLYREQUEST></HEADER><BODY><IMPORTDATA>");
        sb.Append("<REQUESTDESC><REPORTNAME>All Masters</REPORTNAME></REQUESTDESC><REQUESTDATA>");
        foreach (var a in accounts.Where(a => used.Contains(a.Id)).OrderBy(a => a.Code))
            sb.Append($"<TALLYMESSAGE><LEDGER NAME=\"{X(Name(a.Id))}\" ACTION=\"Create\"><NAME>{X(Name(a.Id))}</NAME><PARENT>{X(Group(a))}</PARENT></LEDGER></TALLYMESSAGE>");
        sb.Append("</REQUESTDATA></IMPORTDATA><IMPORTDATA><REQUESTDESC><REPORTNAME>Vouchers</REPORTNAME></REQUESTDESC><REQUESTDATA>");
        foreach (var e in entries)
        {
            sb.Append($"<TALLYMESSAGE><VOUCHER VCHTYPE=\"Journal\" ACTION=\"Create\"><DATE>{e.Date:yyyyMMdd}</DATE><VOUCHERTYPENAME>Journal</VOUCHERTYPENAME>");
            sb.Append($"<VOUCHERNUMBER>{X(e.EntryNo)}</VOUCHERNUMBER><REFERENCE>{X(e.SourceNo ?? "")}</REFERENCE><NARRATION>{X(e.Narration)}</NARRATION>");
            foreach (var l in e.Lines.OrderByDescending(l => l.Debit))
            {
                var debit = l.Debit > 0;
                var amount = debit ? -l.Debit : l.Credit;
                sb.Append($"<ALLLEDGERENTRIES.LIST><LEDGERNAME>{X(Name(l.AccountId))}</LEDGERNAME><ISDEEMEDPOSITIVE>{(debit ? "Yes" : "No")}</ISDEEMEDPOSITIVE>");
                sb.Append($"<AMOUNT>{amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}</AMOUNT></ALLLEDGERENTRIES.LIST>");
            }
            sb.Append("</VOUCHER></TALLYMESSAGE>");
        }
        sb.Append("</REQUESTDATA></IMPORTDATA></BODY></ENVELOPE>");
        audit.Record("accounting.tally_export", nameof(JournalEntry), null, $"{from:yyyy-MM-dd}..{to:yyyy-MM-dd}, {entries.Count} vouchers");
        await db.SaveChangesAsync(ct);
        return sb.ToString();
    }

    /// <summary>Flat journal export (one row per line) for an accountant / import into other accounting software.</summary>
    public async Task<string> ExportCsvAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        await SyncAsync(ct);
        var rows = await db.JournalLines.AsNoTracking()
            .Join(db.JournalEntries.Where(j => j.BranchId == ctx.BranchId && j.Date >= from && j.Date <= to), l => l.JournalEntryId, j => j.Id,
                (l, j) => new { j.Date, j.EntryNo, j.SourceType, j.SourceNo, j.Narration, l.Account!.Code, l.Account.Name, l.Debit, l.Credit, j.CreatedAt })
            .OrderBy(x => x.Date).ThenBy(x => x.CreatedAt).ThenBy(x => x.EntryNo).ToListAsync(ct);
        static string Q(string? s) => s is null ? "" : s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
        var sb = new StringBuilder("Date,Voucher,Source,Reference,Account Code,Account,Debit,Credit,Narration\r\n");
        foreach (var r in rows)
            sb.Append($"{r.Date:yyyy-MM-dd},{Q(r.EntryNo)},{Q(r.SourceType)},{Q(r.SourceNo)},{Q(r.Code)},{Q(r.Name)},{r.Debit:0.00},{r.Credit:0.00},{Q(r.Narration)}\r\n");
        return sb.ToString();
    }
}
