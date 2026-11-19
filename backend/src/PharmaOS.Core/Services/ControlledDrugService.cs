using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record RegisterEntry(DateTimeOffset At, string Type, string? DocumentNo, string? Party, string? Prescriber, decimal In, decimal Out, decimal Balance);
public record RegisterBatch(Guid ProductId, string ProductName, DrugSchedule Schedule, string BaseUnit, Guid BatchId, string BatchNo, DateOnly ExpiryDate,
    decimal Opening, decimal Received, decimal Issued, decimal Closing, List<RegisterEntry> Entries);

/// <summary>
/// Running register for narcotic/psychotropic and Schedule X items, per batch: opening, every receipt and issue with its document,
/// party and prescriber, and closing balance, built from the immutable stock ledger. The statutory format (NDPS / state rules)
/// must be confirmed with the drug controller — this is the data, not a certified register.
/// </summary>
public class ControlledDrugService(PharmaDbContext db, ITenantContext ctx)
{
    public async Task<List<RegisterBatch>> RegisterAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        Require(to >= from && to.DayNumber - from.DayNumber <= 366, "Choose a range of up to a year.");
        var start = BusinessCalendar.StartOfDayUtc(from); var end = BusinessCalendar.StartOfDayUtc(to.AddDays(1));
        var products = await db.Products.AsNoTracking().Where(p => p.Schedule == DrugSchedule.X || p.Schedule == DrugSchedule.NarcoticPsychotropic)
            .ToDictionaryAsync(p => p.Id, ct);
        var ids = products.Keys.ToList();
        var opening = await db.StockMovements.AsNoTracking().Where(m => m.BranchId == ctx.BranchId && ids.Contains(m.ProductId) && m.CreatedAt < start)
            .GroupBy(m => m.BatchId).Select(g => new { g.Key, Q = g.Sum(m => m.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var moves = await db.StockMovements.AsNoTracking().Where(m => m.BranchId == ctx.BranchId && ids.Contains(m.ProductId) && m.CreatedAt >= start && m.CreatedAt < end)
            .OrderBy(m => m.CreatedAt).ToListAsync(ct);

        // documents behind the movements
        Guid[] Src(string type) => moves.Where(m => m.SourceType == type).Select(m => m.SourceId).Distinct().ToArray();
        var (srcSalesInvoice, srcSalesReturn, srcPurchaseInvoice, srcTradeInvoice, srcWardIssue) =
            (Src(nameof(SalesInvoice)), Src(nameof(SalesReturn)), Src(nameof(PurchaseInvoice)), Src(nameof(TradeInvoice)), Src(nameof(WardIssue)));
        var sales = await db.SalesInvoices.AsNoTracking().Where(s => srcSalesInvoice.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => (No: s.InvoiceNo, Party: s.CustomerName, Dr: s.DoctorName), ct);
        var returns = await db.SalesReturns.AsNoTracking().Where(s => srcSalesReturn.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.ReturnNo, ct);
        var purchases = await db.PurchaseInvoices.AsNoTracking().Include(p => p.Supplier).Where(p => srcPurchaseInvoice.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => (No: p.SupplierInvoiceNo, Party: p.Supplier!.Name), ct);
        var trade = await db.TradeInvoices.AsNoTracking().Include(t => t.Retailer).Where(t => srcTradeInvoice.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => (No: t.InvoiceNo, Party: t.Retailer!.Name), ct);
        var ward = await db.WardIssues.AsNoTracking().Include(w => w.Department).Where(w => srcWardIssue.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => (No: w.IssueNo, Party: w.Department!.Name), ct);

        (string? No, string? Party, string? Dr) Doc(StockMovement m) => m.SourceType switch
        {
            nameof(SalesInvoice) when sales.TryGetValue(m.SourceId, out var s) => (s.No, s.Party, s.Dr),
            nameof(SalesReturn) when returns.TryGetValue(m.SourceId, out var r) => (r, null, null),
            nameof(PurchaseInvoice) when purchases.TryGetValue(m.SourceId, out var p) => (p.No, p.Party, null),
            nameof(TradeInvoice) when trade.TryGetValue(m.SourceId, out var t) => (t.No, t.Party, null),
            nameof(WardIssue) when ward.TryGetValue(m.SourceId, out var w) => (w.No, w.Party, null),
            _ => (null, m.Note, null),
        };

        var batchIds = opening.Keys.Concat(moves.Select(m => m.BatchId)).Distinct().ToList();
        var batches = await db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var result = new List<RegisterBatch>();
        foreach (var bid in batchIds)
        {
            var b = batches[bid]; var p = products[b.ProductId];
            var open = opening.GetValueOrDefault(bid);
            var mine = moves.Where(m => m.BatchId == bid).ToList();
            if (open == 0 && mine.Count == 0) continue;
            var bal = open;
            var entries = mine.Select(m =>
            {
                bal += m.QtyUnits;
                var (no, party, dr) = Doc(m);
                return new RegisterEntry(m.CreatedAt, m.Type.ToString(), no, party, dr, m.QtyUnits > 0 ? m.QtyUnits : 0, m.QtyUnits < 0 ? -m.QtyUnits : 0, bal);
            }).ToList();
            result.Add(new RegisterBatch(p.Id, p.Name, p.Schedule, p.BaseUnit, bid, b.BatchNo, b.ExpiryDate, open, entries.Sum(e => e.In), entries.Sum(e => e.Out), bal, entries));
        }
        return result.OrderBy(r => r.ProductName).ThenBy(r => r.ExpiryDate).ToList();
    }
}
