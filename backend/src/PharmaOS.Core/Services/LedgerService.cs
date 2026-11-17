using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record AllocationInput(Guid DocumentId, decimal Amount);
public record SupplierPaymentRequest(Guid SupplierId, DateOnly PaymentDate, SettlementMode Mode, decimal Amount, string? Reference,
    string? Notes, List<AllocationInput>? Allocations);
public record CustomerReceiptRequest(Guid CustomerId, DateOnly ReceiptDate, SettlementMode Mode, decimal Amount, string? Reference,
    List<AllocationInput>? Allocations);

public record OpenDocument(Guid Id, string DocumentNo, DateOnly Date, DateOnly DueDate, int DaysOverdue, decimal Total, decimal Settled, decimal Outstanding, DateTimeOffset EnteredAt);
public record PartyBalance(Guid PartyId, string Name, string? Phone, decimal Billed, decimal Paid, decimal Credits, decimal Balance,
    decimal Overdue, DateOnly? OldestDue, decimal? CreditLimit);
public record StatementLine(DateOnly Date, string Type, string DocumentNo, decimal Debit, decimal Credit, decimal Balance, string? Note);
public record Statement(Guid PartyId, string Name, decimal Balance, decimal UnallocatedCredit, List<StatementLine> Lines, List<OpenDocument> OpenDocuments);
public record AgingReport(List<AgingBucket> Buckets, decimal UnallocatedCredit, decimal NetOutstanding);
public record LedgerDocDto(Guid Id, string Number, Guid PartyId, string PartyName, DateOnly Date, SettlementMode Mode, decimal Amount,
    decimal Allocated, string? Reference, DocStatus Status, string? CancelReason);

/// <summary>
/// Payables (what we owe suppliers) and receivables (what credit customers owe us).
/// Balances are always derived from posted documents — never stored — so they cannot drift.
///   Supplier balance = posted purchases − posted payments − accepted return credit notes
///   Customer balance = credit portion of posted sales − credit-note refunds on returns − posted receipts
/// </summary>
public class LedgerService(PharmaDbContext db, ITenantContext ctx, IClock clock, SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);

    // ===================== PAYABLES =====================

    private async Task<List<OpenDocument>> SupplierInvoicesAsync(Guid? supplierId, CancellationToken ct)
    {
        var q = db.PurchaseInvoices.AsNoTracking().Where(p => p.BranchId == ctx.BranchId && p.Status == PurchaseStatus.Posted);
        if (supplierId is not null) q = q.Where(p => p.SupplierId == supplierId);
        var rows = await q.Select(p => new
        {
            p.Id, p.SupplierId, p.SupplierInvoiceNo, p.InvoiceDate, p.NetAmount, p.Supplier!.PaymentTermsDays, p.CreatedAt,
            Paid = db.SupplierPaymentAllocations
                .Where(a => a.PurchaseInvoiceId == p.Id && db.SupplierPayments.Any(sp => sp.Id == a.SupplierPaymentId && sp.Status == DocStatus.Posted))
                .Sum(a => (decimal?)a.Amount) ?? 0,
        }).ToListAsync(ct);
        var today = Today;
        return rows.Select(r =>
        {
            var due = r.InvoiceDate.AddDays(r.PaymentTermsDays);
            return new OpenDocument(r.Id, r.SupplierInvoiceNo, r.InvoiceDate, due, Math.Max(0, today.DayNumber - due.DayNumber),
                r.NetAmount, r.Paid, r.NetAmount - r.Paid, r.CreatedAt);
        }).ToList();
    }

    private async Task<Dictionary<Guid, (decimal Payments, decimal Allocated, decimal Credits)>> SupplierCreditsAsync(Guid? supplierId, CancellationToken ct)
    {
        var pay = db.SupplierPayments.AsNoTracking().Where(p => p.BranchId == ctx.BranchId && p.Status == DocStatus.Posted);
        var ret = db.PurchaseReturns.AsNoTracking().Where(r => r.BranchId == ctx.BranchId && r.Status == PurchaseReturnStatus.Settled);
        if (supplierId is not null) { pay = pay.Where(p => p.SupplierId == supplierId); ret = ret.Where(r => r.SupplierId == supplierId); }
        var payments = await pay.Select(p => new { p.SupplierId, p.Amount, Allocated = p.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 }).ToListAsync(ct);
        var credits = await ret.GroupBy(r => r.SupplierId).Select(g => new { g.Key, Amount = g.Sum(x => x.AcceptedAmount) }).ToListAsync(ct);
        var ids = payments.Select(p => p.SupplierId).Concat(credits.Select(c => c.Key)).Distinct();
        return ids.ToDictionary(id => id, id => (
            payments.Where(p => p.SupplierId == id).Sum(p => p.Amount),
            payments.Where(p => p.SupplierId == id).Sum(p => p.Allocated),
            credits.Where(c => c.Key == id).Sum(c => c.Amount)));
    }

    public async Task<List<PartyBalance>> PayablesSummaryAsync(CancellationToken ct = default)
    {
        var invoices = await db.PurchaseInvoices.AsNoTracking().Where(p => p.BranchId == ctx.BranchId && p.Status == PurchaseStatus.Posted)
            .Select(p => new { p.Id, p.SupplierId }).ToListAsync(ct);
        var open = (await SupplierInvoicesAsync(null, ct)).ToDictionary(o => o.Id);
        var credits = await SupplierCreditsAsync(null, ct);
        var supplierIds = invoices.Select(i => i.SupplierId).Concat(credits.Keys).Distinct().ToList();
        var suppliers = await db.Suppliers.AsNoTracking().Where(s => supplierIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, ct);

        return supplierIds.Select(id =>
        {
            var docs = invoices.Where(i => i.SupplierId == id).Select(i => open[i.Id]).ToList();
            var (payments, _, cn) = credits.GetValueOrDefault(id);
            var billed = docs.Sum(d => d.Total);
            var overdue = docs.Where(d => d.DaysOverdue > 0 && d.Outstanding > 0).ToList();
            return new PartyBalance(id, suppliers[id].Name, suppliers[id].Phone, billed, payments, cn, billed - payments - cn,
                overdue.Sum(d => d.Outstanding), docs.Where(d => d.Outstanding > 0).OrderBy(d => d.DueDate).FirstOrDefault()?.DueDate, null);
        }).Where(b => b.Balance != 0 || b.Billed != 0).OrderByDescending(b => b.Balance).ToList();
    }

    public async Task<AgingReport> PayablesAgingAsync(CancellationToken ct = default)
    {
        var docs = (await SupplierInvoicesAsync(null, ct)).Where(d => d.Outstanding > 0).ToList();
        var credits = await SupplierCreditsAsync(null, ct);
        var unallocated = credits.Values.Sum(c => c.Payments - c.Allocated + c.Credits);
        return BuildAging(docs, unallocated);
    }

    public async Task<Statement> SupplierStatementAsync(Guid supplierId, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().SingleOrDefaultAsync(s => s.Id == supplierId, ct) ?? throw BusinessException.NotFound("Supplier");
        var docs = await SupplierInvoicesAsync(supplierId, ct);
        var payments = await db.SupplierPayments.AsNoTracking()
            .Where(p => p.BranchId == ctx.BranchId && p.SupplierId == supplierId && p.Status == DocStatus.Posted)
            .Select(p => new { p.PaymentDate, p.PaymentNo, p.Amount, p.Mode, p.Reference, Allocated = p.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 })
            .ToListAsync(ct);
        var creditNotes = await db.PurchaseReturns.AsNoTracking()
            .Where(r => r.BranchId == ctx.BranchId && r.SupplierId == supplierId && r.Status == PurchaseReturnStatus.Settled)
            .Select(r => new { r.CreditNoteDate, r.SupplierCreditNoteNo, r.ReturnNo, r.AcceptedAmount }).ToListAsync(ct);

        var entries = docs.Select(d => (d.Date, Type: "Purchase", No: d.DocumentNo, Debit: 0m, Credit: d.Total, Note: (string?)null))
            .Concat(payments.Select(p => (p.PaymentDate, Type: "Payment", No: p.PaymentNo, Debit: p.Amount, Credit: 0m, Note: (string?)$"{p.Mode} {p.Reference}".Trim())))
            .Concat(creditNotes.Select(c => (c.CreditNoteDate!.Value, Type: "Credit note", No: c.SupplierCreditNoteNo ?? c.ReturnNo, Debit: c.AcceptedAmount, Credit: 0m, Note: (string?)c.ReturnNo)))
            .OrderBy(e => e.Item1).ToList();
        var balance = 0m;
        var lines = entries.Select(e => { balance += e.Credit - e.Debit; return new StatementLine(e.Item1, e.Type, e.No, e.Debit, e.Credit, balance, e.Note); }).ToList();
        var unallocated = payments.Sum(p => p.Amount - p.Allocated) + creditNotes.Sum(c => c.AcceptedAmount);
        return new Statement(supplierId, supplier.Name, balance, unallocated, lines,
            docs.Where(d => d.Outstanding > 0).OrderBy(d => d.DueDate).ToList());
    }

    public async Task<LedgerDocDto> CreateSupplierPaymentAsync(SupplierPaymentRequest r, CancellationToken ct = default)
    {
        Require(r.Amount > 0, "Payment amount must be positive.");
        Require(Enum.IsDefined(r.Mode), "Invalid payment mode.");
        Require(r.PaymentDate <= Today, "Payment date cannot be in the future.");
        Require(r.Mode == SettlementMode.Cash || !string.IsNullOrWhiteSpace(r.Reference), "Reference (UTR / cheque no.) is required for non-cash payments.");
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == r.SupplierId, ct) ?? throw BusinessException.NotFound("Supplier");
        var amount = Money.R2(r.Amount);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var open = (await SupplierInvoicesAsync(supplier.Id, ct)).Where(d => d.Outstanding > 0).ToDictionary(d => d.Id);
        var allocations = Allocate(r.Allocations, open, amount, "invoice");

        var payment = new SupplierPayment
        {
            BranchId = ctx.BranchId, SupplierId = supplier.Id, PaymentDate = r.PaymentDate, Mode = r.Mode, Amount = amount,
            Reference = r.Reference?.Trim(), Notes = r.Notes?.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
        };
        payment.Allocations = allocations.Select(a => new SupplierPaymentAllocation { SupplierPaymentId = payment.Id, PurchaseInvoiceId = a.DocumentId, Amount = a.Amount }).ToList();
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        payment.PaymentNo = SequenceService.Format(branch.Code, "P", r.PaymentDate, await sequences.NextAsync(branch.Id, "P", r.PaymentDate, ct));
        db.SupplierPayments.Add(payment);
        audit.Record("supplier_payment.posted", nameof(SupplierPayment), payment.Id, $"{payment.PaymentNo} {supplier.Name} {amount} {r.Mode}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(payment, supplier.Name);
    }

    public async Task<LedgerDocDto> CancelSupplierPaymentAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        var p = await db.SupplierPayments.Include(x => x.Allocations).Include(x => x.Supplier)
                    .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Payment");
        if (p.Status != DocStatus.Posted) throw BusinessException.Conflict("not_posted", "Payment is already cancelled.");
        p.Status = DocStatus.Cancelled;
        p.CancelReason = reason.Trim();
        audit.Record("supplier_payment.cancelled", nameof(SupplierPayment), p.Id, $"{p.PaymentNo}: {reason}");
        await db.SaveChangesAsync(ct);
        return ToDto(p, p.Supplier!.Name);
    }

    public async Task<PagedResult<LedgerDocDto>> ListSupplierPaymentsAsync(Guid? supplierId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.SupplierPayments.AsNoTracking().Include(p => p.Allocations).Include(p => p.Supplier).Where(p => p.BranchId == ctx.BranchId);
        if (supplierId is not null) q = q.Where(p => p.SupplierId == supplierId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<LedgerDocDto>(rows.Select(p => ToDto(p, p.Supplier!.Name)).ToList(), total, page, pageSize);
    }

    private static LedgerDocDto ToDto(SupplierPayment p, string name) => new(p.Id, p.PaymentNo, p.SupplierId, name, p.PaymentDate, p.Mode,
        p.Amount, p.Allocations.Sum(a => a.Amount), p.Reference, p.Status, p.CancelReason);

    // ===================== RECEIVABLES =====================

    private async Task<List<OpenDocument>> CustomerInvoicesAsync(Guid? customerId, CancellationToken ct)
    {
        var q = db.SalesInvoices.AsNoTracking().Where(s => s.BranchId == ctx.BranchId && s.Status == SalesStatus.Posted && s.CustomerId != null
                                                         && s.Payments.Any(p => p.Mode == PaymentMode.Credit));
        if (customerId is not null) q = q.Where(s => s.CustomerId == customerId);
        var rows = await q.Select(s => new
        {
            s.Id, s.InvoiceNo, s.BusinessDate, s.InvoiceAt,
            Credit = s.Payments.Where(p => p.Mode == PaymentMode.Credit).Sum(p => p.Amount),
            Refunded = db.SalesReturns.Where(r => r.SalesInvoiceId == s.Id && r.RefundMode == PaymentMode.Credit).Sum(r => (decimal?)r.RefundAmount) ?? 0,
            Received = db.CustomerReceiptAllocations
                .Where(a => a.SalesInvoiceId == s.Id && db.CustomerReceipts.Any(cr => cr.Id == a.CustomerReceiptId && cr.Status == DocStatus.Posted))
                .Sum(a => (decimal?)a.Amount) ?? 0,
        }).ToListAsync(ct);
        var today = Today;
        return rows.Select(r => new OpenDocument(r.Id, r.InvoiceNo, r.BusinessDate, r.BusinessDate,
            Math.Max(0, today.DayNumber - r.BusinessDate.DayNumber), r.Credit - r.Refunded, r.Received, r.Credit - r.Refunded - r.Received, r.InvoiceAt)).ToList();
    }

    /// <summary>Current balance for one customer (used for credit-limit checks at billing).</summary>
    public async Task<decimal> CustomerBalanceAsync(Guid customerId, CancellationToken ct = default)
    {
        var docs = await CustomerInvoicesAsync(customerId, ct);
        var creditRefundsOnCash = await CreditRefundsOutsideCreditInvoicesAsync(customerId, ct);
        var receipts = await db.CustomerReceipts.Where(r => r.BranchId == ctx.BranchId && r.CustomerId == customerId && r.Status == DocStatus.Posted)
            .Select(r => new { r.Amount, Allocated = r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 }).ToListAsync(ct);
        return docs.Sum(d => d.Outstanding) - receipts.Sum(r => r.Amount - r.Allocated) - creditRefundsOnCash;
    }

    /// <summary>Credit-note refunds issued against invoices that had no credit portion (customer advance balance).</summary>
    private async Task<decimal> CreditRefundsOutsideCreditInvoicesAsync(Guid? customerId, CancellationToken ct)
    {
        var q = db.SalesReturns.Where(r => r.BranchId == ctx.BranchId && r.RefundMode == PaymentMode.Credit)
            .Join(db.SalesInvoices.Where(s => s.Status == SalesStatus.Posted && !s.Payments.Any(p => p.Mode == PaymentMode.Credit)),
                r => r.SalesInvoiceId, s => s.Id, (r, s) => new { s.CustomerId, r.RefundAmount });
        if (customerId is not null) q = q.Where(x => x.CustomerId == customerId);
        return await q.SumAsync(x => (decimal?)x.RefundAmount, ct) ?? 0;
    }

    public async Task<List<PartyBalance>> ReceivablesSummaryAsync(CancellationToken ct = default)
    {
        var docs = await db.SalesInvoices.AsNoTracking()
            .Where(s => s.BranchId == ctx.BranchId && s.Status == SalesStatus.Posted && s.CustomerId != null && s.Payments.Any(p => p.Mode == PaymentMode.Credit))
            .Select(s => new { s.Id, CustomerId = s.CustomerId!.Value }).ToListAsync(ct);
        var open = (await CustomerInvoicesAsync(null, ct)).ToDictionary(o => o.Id);
        var receipts = await db.CustomerReceipts.AsNoTracking().Where(r => r.BranchId == ctx.BranchId && r.Status == DocStatus.Posted)
            .Select(r => new { r.CustomerId, r.Amount, Allocated = r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 }).ToListAsync(ct);
        var advanceRefunds = await db.SalesReturns.Where(r => r.BranchId == ctx.BranchId && r.RefundMode == PaymentMode.Credit)
            .Join(db.SalesInvoices.Where(s => s.Status == SalesStatus.Posted && !s.Payments.Any(p => p.Mode == PaymentMode.Credit)),
                r => r.SalesInvoiceId, s => s.Id, (r, s) => new { CustomerId = s.CustomerId!.Value, r.RefundAmount }).ToListAsync(ct);

        var ids = docs.Select(d => d.CustomerId).Concat(receipts.Select(r => r.CustomerId)).Concat(advanceRefunds.Select(a => a.CustomerId)).Distinct().ToList();
        var customers = await db.Customers.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, ct);
        return ids.Select(id =>
        {
            var mine = docs.Where(d => d.CustomerId == id).Select(d => open[d.Id]).ToList();
            var rc = receipts.Where(r => r.CustomerId == id).ToList();
            var adv = advanceRefunds.Where(a => a.CustomerId == id).Sum(a => a.RefundAmount);
            var billed = mine.Sum(d => d.Total);
            var paid = rc.Sum(r => r.Amount);
            var balance = billed - paid - adv;
            return new PartyBalance(id, customers[id].Name, customers[id].Phone, billed, paid, adv, balance,
                mine.Where(d => d.DaysOverdue > 30 && d.Outstanding > 0).Sum(d => d.Outstanding),
                mine.Where(d => d.Outstanding > 0).OrderBy(d => d.Date).FirstOrDefault()?.Date, customers[id].CreditLimit);
        }).Where(b => b.Balance != 0).OrderByDescending(b => b.Balance).ToList();
    }

    public async Task<AgingReport> ReceivablesAgingAsync(CancellationToken ct = default)
    {
        var docs = (await CustomerInvoicesAsync(null, ct)).Where(d => d.Outstanding > 0).ToList();
        var unallocated = (await db.CustomerReceipts.Where(r => r.BranchId == ctx.BranchId && r.Status == DocStatus.Posted)
                .Select(r => r.Amount - (r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0)).ToListAsync(ct)).Sum()
            + await CreditRefundsOutsideCreditInvoicesAsync(null, ct);
        return BuildAging(docs, unallocated);
    }

    public async Task<Statement> CustomerStatementAsync(Guid customerId, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.Id == customerId, ct) ?? throw BusinessException.NotFound("Customer");
        var docs = await CustomerInvoicesAsync(customerId, ct);
        var invoiceIds = await db.SalesInvoices.Where(s => s.CustomerId == customerId && s.Status == SalesStatus.Posted).Select(s => s.Id).ToListAsync(ct);
        var refunds = await db.SalesReturns.AsNoTracking()
            .Where(r => r.BranchId == ctx.BranchId && r.RefundMode == PaymentMode.Credit && invoiceIds.Contains(r.SalesInvoiceId))
            .Select(r => new { r.BusinessDate, r.ReturnNo, r.RefundAmount }).ToListAsync(ct);
        var receipts = await db.CustomerReceipts.AsNoTracking()
            .Where(r => r.BranchId == ctx.BranchId && r.CustomerId == customerId && r.Status == DocStatus.Posted)
            .Select(r => new { r.ReceiptDate, r.ReceiptNo, r.Amount, r.Mode, Allocated = r.Allocations.Sum(a => (decimal?)a.Amount) ?? 0 }).ToListAsync(ct);
        var docIds = docs.Select(d => d.Id).ToList();
        var creditSales = await db.SalesInvoices.AsNoTracking().Where(s => docIds.Contains(s.Id))
            .Select(s => new { s.BusinessDate, s.InvoiceNo, Credit = s.Payments.Where(p => p.Mode == PaymentMode.Credit).Sum(p => p.Amount) }).ToListAsync(ct);

        var entries = creditSales.Select(s => (s.BusinessDate, Type: "Credit sale", No: s.InvoiceNo, Debit: s.Credit, Credit: 0m, Note: (string?)null))
            .Concat(refunds.Select(r => (r.BusinessDate, Type: "Return (credit note)", No: r.ReturnNo, Debit: 0m, Credit: r.RefundAmount, Note: (string?)null)))
            .Concat(receipts.Select(r => (r.ReceiptDate, Type: "Receipt", No: r.ReceiptNo, Debit: 0m, Credit: r.Amount, Note: (string?)r.Mode.ToString())))
            .OrderBy(e => e.Item1).ToList();
        var balance = 0m;
        var lines = entries.Select(e => { balance += e.Debit - e.Credit; return new StatementLine(e.Item1, e.Type, e.No, e.Debit, e.Credit, balance, e.Note); }).ToList();
        return new Statement(customerId, customer.Name, balance,
            receipts.Sum(r => r.Amount - r.Allocated) + await CreditRefundsOutsideCreditInvoicesAsync(customerId, ct), lines,
            docs.Where(d => d.Outstanding > 0).OrderBy(d => d.Date).ToList());
    }

    public async Task<LedgerDocDto> CreateCustomerReceiptAsync(CustomerReceiptRequest r, CancellationToken ct = default)
    {
        Require(r.Amount > 0, "Receipt amount must be positive.");
        Require(Enum.IsDefined(r.Mode), "Invalid mode.");
        Require(r.ReceiptDate <= Today, "Receipt date cannot be in the future.");
        var customer = await db.Customers.SingleOrDefaultAsync(c => c.Id == r.CustomerId, ct) ?? throw BusinessException.NotFound("Customer");
        var amount = Money.R2(r.Amount);

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var open = (await CustomerInvoicesAsync(customer.Id, ct)).Where(d => d.Outstanding > 0).ToDictionary(d => d.Id);
        var allocations = Allocate(r.Allocations, open, amount, "bill");
        var receipt = new CustomerReceipt
        {
            BranchId = ctx.BranchId, CustomerId = customer.Id, ReceiptDate = r.ReceiptDate, Mode = r.Mode, Amount = amount,
            Reference = r.Reference?.Trim(), CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
        };
        receipt.Allocations = allocations.Select(a => new CustomerReceiptAllocation { CustomerReceiptId = receipt.Id, SalesInvoiceId = a.DocumentId, Amount = a.Amount }).ToList();
        var branch = await db.Branches.SingleAsync(b => b.Id == ctx.BranchId, ct);
        receipt.ReceiptNo = SequenceService.Format(branch.Code, "C", r.ReceiptDate, await sequences.NextAsync(branch.Id, "C", r.ReceiptDate, ct));
        db.CustomerReceipts.Add(receipt);
        audit.Record("customer_receipt.posted", nameof(CustomerReceipt), receipt.Id, $"{receipt.ReceiptNo} {customer.Name} {amount} {r.Mode}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(receipt, customer.Name);
    }

    public async Task<LedgerDocDto> CancelCustomerReceiptAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        var r = await db.CustomerReceipts.Include(x => x.Allocations).Include(x => x.Customer)
                    .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Receipt");
        if (r.Status != DocStatus.Posted) throw BusinessException.Conflict("not_posted", "Receipt is already cancelled.");
        r.Status = DocStatus.Cancelled;
        r.CancelReason = reason.Trim();
        audit.Record("customer_receipt.cancelled", nameof(CustomerReceipt), r.Id, $"{r.ReceiptNo}: {reason}");
        await db.SaveChangesAsync(ct);
        return ToDto(r, r.Customer!.Name);
    }

    public async Task<PagedResult<LedgerDocDto>> ListCustomerReceiptsAsync(Guid? customerId, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.CustomerReceipts.AsNoTracking().Include(p => p.Allocations).Include(p => p.Customer).Where(p => p.BranchId == ctx.BranchId);
        if (customerId is not null) q = q.Where(p => p.CustomerId == customerId);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.ReceiptDate).ThenByDescending(p => p.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return new PagedResult<LedgerDocDto>(rows.Select(p => ToDto(p, p.Customer!.Name)).ToList(), total, page, pageSize);
    }

    private static LedgerDocDto ToDto(CustomerReceipt p, string name) => new(p.Id, p.ReceiptNo, p.CustomerId, name, p.ReceiptDate, p.Mode,
        p.Amount, p.Allocations.Sum(a => a.Amount), p.Reference, p.Status, p.CancelReason);

    // ===================== shared =====================

    /// <summary>Explicit allocations are validated; when none are given, the amount is applied oldest-due first.</summary>
    private static List<AllocationInput> Allocate(List<AllocationInput>? requested, Dictionary<Guid, OpenDocument> open, decimal amount, string what)
    {
        if (requested is { Count: > 0 })
        {
            var merged = requested.GroupBy(a => a.DocumentId).Select(g => new AllocationInput(g.Key, Money.R2(g.Sum(x => x.Amount)))).ToList();
            foreach (var a in merged)
            {
                Require(a.Amount > 0, "Allocation amounts must be positive.");
                if (!open.TryGetValue(a.DocumentId, out var doc)) throw BusinessException.Validation($"Selected {what} is not open for this party.");
                if (a.Amount > doc.Outstanding) throw BusinessException.Validation($"Allocation to {doc.DocumentNo} exceeds its outstanding {doc.Outstanding:0.00}.");
            }
            if (merged.Sum(a => a.Amount) > amount) throw BusinessException.Validation("Allocations exceed the amount.");
            return merged;
        }
        var result = new List<AllocationInput>();
        var left = amount;
        foreach (var d in open.Values.OrderBy(d => d.DueDate).ThenBy(d => d.Date).ThenBy(d => d.EnteredAt)) // deterministic on ties
        {
            if (left <= 0) break;
            var take = Math.Min(left, d.Outstanding);
            result.Add(new AllocationInput(d.Id, take));
            left -= take;
        }
        return result;
    }

    private static AgingReport BuildAging(List<OpenDocument> docs, decimal unallocated)
    {
        AgingBucket B(string label, Func<OpenDocument, bool> f) { var x = docs.Where(f).ToList(); return new AgingBucket(label, x.Sum(d => d.Outstanding), x.Count); }
        var buckets = new List<AgingBucket>
        {
            B("Not due", d => d.DaysOverdue == 0), B("1-30 days", d => d.DaysOverdue is >= 1 and <= 30),
            B("31-60 days", d => d.DaysOverdue is >= 31 and <= 60), B("61-90 days", d => d.DaysOverdue is >= 61 and <= 90),
            B("90+ days", d => d.DaysOverdue > 90),
        };
        return new AgingReport(buckets, unallocated, docs.Sum(d => d.Outstanding) - unallocated);
    }
}
