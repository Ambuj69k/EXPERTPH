using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- contracts ----------

public record ReorderSuggestion(Guid ProductId, string ProductName, string? Manufacturer, decimal PackSize, string PackUnit,
    decimal SoldUnits, decimal AvgDailyUnits, decimal OnHandUnits, decimal OnOrderUnits, decimal ReorderLevelUnits,
    decimal TargetUnits, decimal SuggestedPacks, Guid? LastSupplierId, string? LastSupplierName, decimal? LastRate);

public record PoLineInput(Guid ProductId, decimal QtyPacks, decimal? ExpectedRate);
public record PurchaseOrderInput(Guid SupplierId, DateOnly? ExpectedDate, string? Notes, List<PoLineInput> Lines);
public record PoLineDto(Guid Id, int LineNo, Guid ProductId, string ProductName, decimal PackSize, decimal QtyPacks, decimal ReceivedQtyPacks,
    decimal PendingQtyPacks, decimal? ExpectedRate, decimal GstRate);
public record PurchaseOrderDto(Guid Id, string PoNo, Guid SupplierId, string SupplierName, string? SupplierPhone, DateOnly OrderDate,
    DateOnly? ExpectedDate, PurchaseOrderStatus Status, string? Notes, DateTimeOffset CreatedAt, DateTimeOffset? SentAt, int Version,
    decimal EstimatedValue, List<PoLineDto> Lines, List<PurchaseListItem> Receipts, string ShareText);
public record PurchaseOrderListItem(Guid Id, string PoNo, string SupplierName, DateOnly OrderDate, DateOnly? ExpectedDate,
    PurchaseOrderStatus Status, int LineCount, decimal OrderedPacks, decimal ReceivedPacks);

public record ImportMapping(string? ItemCode, string ItemName, string? Batch, string? Expiry, string Qty, string? Free, string? Mrp,
    string? Rate, string? Discount, string? Gst, string? Pack);
public record ImportPreviewRequest(Guid SupplierId, string CsvText, ImportMapping? Mapping, bool SaveMapping);
public record ProductSuggestion(Guid ProductId, string Name, decimal PackSize);
public record ImportRow(int RowNo, string? ItemCode, string ItemName, Guid? ProductId, string? ProductName, string? MatchedBy,
    List<ProductSuggestion> Suggestions, string? BatchNo, DateOnly? ExpiryDate, decimal QtyPacks, decimal FreeQtyPacks, decimal? Mrp,
    decimal? Rate, decimal DiscountPercent, decimal? GstRate, List<string> Issues);
public record ImportPreview(Guid SupplierId, List<string> Headers, ImportMapping Mapping, List<ImportRow> Rows, int Matched, int Unmatched);
public record ItemMapInput(string? SupplierItemCode, string SupplierItemName, Guid ProductId);

/// <summary>Reorder suggestions and purchase orders.</summary>
public class PurchasingService(PharmaDbContext db, ITenantContext ctx, IClock clock, SequenceService sequences, AuditService audit)
{
    private DateOnly Today => BusinessCalendar.LocalDate(clock.UtcNow);
    private static readonly PurchaseOrderStatus[] OpenStatuses = [PurchaseOrderStatus.Sent, PurchaseOrderStatus.PartiallyReceived];

    /// <summary>
    /// For each active product:
    ///   SoldUnits   = units sold on posted bills in the last <paramref name="lookbackDays"/> days − units returned in that window
    ///   AvgDaily    = SoldUnits ÷ lookbackDays
    ///   Target      = max(ceil(AvgDaily × coverDays), ReorderLevelUnits)
    ///   Need        = Target − OnHand (sellable: not expired, not blocked) − OnOrder (pending packs on Sent/PartiallyReceived POs)
    ///   Suggested   = ceil(Need ÷ PackSize) packs, only when Need &gt; 0
    /// </summary>
    public async Task<List<ReorderSuggestion>> ReorderAsync(int lookbackDays, int coverDays, CancellationToken ct = default)
    {
        Require(lookbackDays is >= 7 and <= 365, "Look-back must be 7-365 days.");
        Require(coverDays is >= 1 and <= 180, "Cover must be 1-180 days.");
        var today = Today;
        var since = today.AddDays(-lookbackDays + 1);
        var b = ctx.BranchId;

        var sold = await db.SalesInvoiceLines.AsNoTracking()
            .Where(l => db.SalesInvoices.Any(s => s.Id == l.SalesInvoiceId && s.BranchId == b && s.Status == SalesStatus.Posted && s.BusinessDate >= since))
            .GroupBy(l => l.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var returned = await db.SalesReturnLines.AsNoTracking()
            .Where(l => db.SalesReturns.Any(r => r.Id == l.SalesReturnId && r.BranchId == b && r.BusinessDate >= since))
            .GroupBy(l => l.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var onHand = await db.StockBalances.AsNoTracking()
            .Where(s => s.BranchId == b && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .GroupBy(s => s.ProductId).Select(g => new { g.Key, Qty = g.Sum(x => x.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Qty, ct);
        var onOrder = await db.PurchaseOrderLines.AsNoTracking()
            .Where(l => db.PurchaseOrders.Any(o => o.Id == l.PurchaseOrderId && o.BranchId == b && OpenStatuses.Contains(o.Status)) && l.QtyPacks > l.ReceivedQtyPacks)
            .GroupBy(l => l.ProductId).Select(g => new { g.Key, Packs = g.Sum(x => x.QtyPacks - x.ReceivedQtyPacks) }).ToDictionaryAsync(x => x.Key, x => x.Packs, ct);

        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).ToListAsync(ct);
        var candidates = new List<(Product P, decimal Sold, decimal Avg, decimal Hand, decimal Order, decimal Target, decimal Packs)>();
        foreach (var p in products)
        {
            var s = Math.Max(0, sold.GetValueOrDefault(p.Id) - returned.GetValueOrDefault(p.Id));
            if (s == 0 && p.ReorderLevelUnits == 0) continue;
            var avg = s / lookbackDays;
            var target = Math.Max(Math.Ceiling(avg * coverDays), p.ReorderLevelUnits);
            var hand = onHand.GetValueOrDefault(p.Id);
            var order = onOrder.GetValueOrDefault(p.Id) * p.PackSize;
            var need = target - hand - order;
            if (need <= 0) continue;
            candidates.Add((p, s, Money.R4(avg), hand, order, target, Math.Ceiling(need / p.PackSize)));
        }

        var ids = candidates.Select(c => c.P.Id).ToList();
        var last = await db.PurchaseInvoiceLines.AsNoTracking().Where(l => ids.Contains(l.ProductId))
            .Join(db.PurchaseInvoices.Where(p => p.BranchId == b && p.Status == PurchaseStatus.Posted), l => l.PurchaseInvoiceId, p => p.Id,
                (l, p) => new { l.ProductId, p.SupplierId, SupplierName = p.Supplier!.Name, l.Rate, p.InvoiceDate, p.CreatedAt })
            .ToListAsync(ct);
        var lastBy = last.GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.InvoiceDate).ThenByDescending(x => x.CreatedAt).First());

        return candidates.Select(c =>
        {
            lastBy.TryGetValue(c.P.Id, out var l);
            return new ReorderSuggestion(c.P.Id, c.P.Name, c.P.Manufacturer, c.P.PackSize, c.P.PackUnit, c.Sold, c.Avg, c.Hand, c.Order,
                c.P.ReorderLevelUnits, c.Target, c.Packs, l?.SupplierId, l?.SupplierName, l?.Rate);
        }).OrderBy(x => x.LastSupplierName ?? "~").ThenBy(x => x.ProductName).ToList();
    }

    public async Task<PurchaseOrderDto> CreateAsync(PurchaseOrderInput input, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == input.SupplierId && s.IsActive, ct) ?? throw BusinessException.NotFound("Supplier");
        Require(input.Lines is { Count: > 0 and <= 500 }, "An order needs 1-500 lines.");
        var productIds = input.Lines.Select(l => l.ProductId).Distinct().ToList();
        Require(productIds.Count == input.Lines.Count, "Each product can appear once per order.");
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var today = Today;
        Require(input.ExpectedDate is null || input.ExpectedDate >= today, "Expected date cannot be in the past.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var branch = await db.Branches.SingleAsync(x => x.Id == ctx.BranchId, ct);
        var po = new PurchaseOrder
        {
            BranchId = branch.Id, SupplierId = supplier.Id, OrderDate = today, ExpectedDate = input.ExpectedDate, Notes = input.Notes?.Trim(),
            CreatedAt = clock.UtcNow, CreatedByUserId = ctx.UserId,
            PoNo = SequenceService.Format(branch.Code, "O", today, await sequences.NextAsync(branch.Id, "O", today, ct)),
        };
        var n = 0;
        foreach (var l in input.Lines)
        {
            n++;
            if (!products.ContainsKey(l.ProductId)) throw BusinessException.Validation($"Line {n}: product not found.");
            Require(l.QtyPacks > 0 && l.QtyPacks == decimal.Truncate(l.QtyPacks), $"Line {n}: quantity must be a whole number of packs.");
            Require(l.ExpectedRate is null or >= 0, $"Line {n}: rate cannot be negative.");
            po.Lines.Add(new PurchaseOrderLine { PurchaseOrderId = po.Id, LineNo = n, ProductId = l.ProductId, QtyPacks = l.QtyPacks, ExpectedRate = l.ExpectedRate });
        }
        db.PurchaseOrders.Add(po);
        audit.Record("po.created", nameof(PurchaseOrder), po.Id, $"{po.PoNo} to {supplier.Name}, {po.Lines.Count} lines");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(po.Id, ct);
    }

    public async Task<PurchaseOrderDto> SetStatusAsync(Guid id, PurchaseOrderStatus to, CancellationToken ct = default)
    {
        var po = await db.PurchaseOrders.SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Purchase order");
        if (to == PurchaseOrderStatus.Cancelled && await db.PurchaseInvoices.AnyAsync(p => p.PurchaseOrderId == id && p.Status != PurchaseStatus.Cancelled, ct))
            throw BusinessException.Conflict("has_receipts", "Goods were already received against this order. Close it instead.");
        var ok = (po.Status, to) switch
        {
            (PurchaseOrderStatus.Draft, PurchaseOrderStatus.Sent) => true,
            (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Sent, PurchaseOrderStatus.Cancelled) => true,
            (PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived, PurchaseOrderStatus.Closed) => true,
            _ => false,
        };
        if (!ok) throw BusinessException.Conflict("invalid_transition", $"Cannot change an order from {po.Status} to {to}.");
        po.Status = to;
        if (to == PurchaseOrderStatus.Sent) po.SentAt = clock.UtcNow;
        audit.Record($"po.{to.ToString().ToLowerInvariant()}", nameof(PurchaseOrder), po.Id, po.PoNo);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<PagedResult<PurchaseOrderListItem>> ListAsync(PurchaseOrderStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = MasterDataService.NormalizePaging(page, pageSize);
        var q = db.PurchaseOrders.AsNoTracking().Where(o => o.BranchId == ctx.BranchId);
        if (status is not null) q = q.Where(o => o.Status == status);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new PurchaseOrderListItem(o.Id, o.PoNo, o.Supplier!.Name, o.OrderDate, o.ExpectedDate, o.Status, o.Lines.Count,
                o.Lines.Sum(l => l.QtyPacks), o.Lines.Sum(l => l.ReceivedQtyPacks)))
            .ToListAsync(ct);
        return new PagedResult<PurchaseOrderListItem>(rows, total, page, pageSize);
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.PurchaseOrders.AsNoTracking().Include(x => x.Supplier).Include(x => x.Lines).ThenInclude(l => l.Product)
                    .AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Purchase order");
        var receipts = await db.PurchaseInvoices.AsNoTracking().Where(p => p.PurchaseOrderId == id)
            .Select(p => new PurchaseListItem(p.Id, p.Supplier!.Name, p.SupplierInvoiceNo, p.InvoiceDate, p.Status, p.NetAmount, p.Lines.Count)).ToListAsync(ct);
        var tenant = await db.Tenants.AsNoTracking().SingleAsync(t => t.Id == ctx.TenantId, ct);
        var lines = o.Lines.OrderBy(l => l.LineNo).Select(l => new PoLineDto(l.Id, l.LineNo, l.ProductId, l.Product!.Name, l.Product.PackSize, l.QtyPacks,
            l.ReceivedQtyPacks, Math.Max(0, l.QtyPacks - l.ReceivedQtyPacks), l.ExpectedRate, l.Product.GstRate)).ToList();
        var share = new StringBuilder($"Order {o.PoNo} from {tenant.Name}\n");
        foreach (var l in lines) share.Append($"{l.LineNo}. {l.ProductName} x {l.QtyPacks:0}\n");
        if (o.ExpectedDate is not null) share.Append($"Needed by {o.ExpectedDate:dd MMM}\n");
        return new PurchaseOrderDto(o.Id, o.PoNo, o.SupplierId, o.Supplier!.Name, o.Supplier.Phone, o.OrderDate, o.ExpectedDate, o.Status, o.Notes,
            o.CreatedAt, o.SentAt, o.Version, Money.R2(lines.Sum(l => l.QtyPacks * (l.ExpectedRate ?? 0))), lines, receipts, share.ToString().TrimEnd());
    }

    /// <summary>Applies (sign = +1) or reverses (sign = −1) a purchase invoice's paid quantities against its order.</summary>
    public async Task ApplyReceiptAsync(PurchaseInvoice invoice, int sign, CancellationToken ct)
    {
        if (invoice.PurchaseOrderId is null) return;
        var po = await db.PurchaseOrders.Include(o => o.Lines).SingleAsync(o => o.Id == invoice.PurchaseOrderId, ct);
        foreach (var g in invoice.Lines.GroupBy(l => l.ProductId))
        {
            var line = po.Lines.SingleOrDefault(l => l.ProductId == g.Key);
            if (line is not null) line.ReceivedQtyPacks = Math.Max(0, line.ReceivedQtyPacks + sign * g.Sum(x => x.QtyPacks));
        }
        if (po.Status is PurchaseOrderStatus.Sent or PurchaseOrderStatus.PartiallyReceived or PurchaseOrderStatus.Received)
            po.Status = po.Lines.All(l => l.ReceivedQtyPacks >= l.QtyPacks) ? PurchaseOrderStatus.Received
                : po.Lines.Any(l => l.ReceivedQtyPacks > 0) ? PurchaseOrderStatus.PartiallyReceived : PurchaseOrderStatus.Sent;
    }

    public async Task ValidateOrderForInvoiceAsync(Guid? orderId, Guid supplierId, CancellationToken ct)
    {
        if (orderId is null) return;
        var po = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.BranchId == ctx.BranchId, ct)
                 ?? throw BusinessException.NotFound("Purchase order");
        if (po.SupplierId != supplierId) throw BusinessException.Validation("The order belongs to a different supplier.");
        if (!OpenStatuses.Contains(po.Status)) throw BusinessException.Conflict("order_not_open", $"Order {po.PoNo} is {po.Status}; it cannot receive goods.");
    }
}

/// <summary>
/// Supplier invoice import from CSV. Produces a preview only — the user reviews every row and a DRAFT purchase is created
/// from the confirmed rows, so nothing reaches stock or payables without a human posting it.
/// </summary>
public partial class ImportService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ImportPreview> PreviewAsync(ImportPreviewRequest r, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(r.CsvText) && r.CsvText.Length <= 2_000_000, "Paste or upload a CSV file (max 2 MB).");
        var supplier = await db.Suppliers.SingleOrDefaultAsync(s => s.Id == r.SupplierId, ct) ?? throw BusinessException.NotFound("Supplier");
        var table = ParseCsv(r.CsvText);
        Require(table.Count >= 2, "The file needs a header row and at least one item row.");
        var headers = table[0].Select(h => h.Trim()).ToList();

        var mapping = r.Mapping
                      ?? (supplier.ImportMappingJson is { } saved ? JsonSerializer.Deserialize<ImportMapping>(saved, Json) : null)
                      ?? GuessMapping(headers);
        Require(mapping is not null && !string.IsNullOrWhiteSpace(mapping.ItemName) && !string.IsNullOrWhiteSpace(mapping.Qty),
            "Map at least the item name and quantity columns.");
        int Col(string? name) => name is null ? -1 : headers.FindIndex(h => string.Equals(h, name, StringComparison.OrdinalIgnoreCase));
        var c = new
        {
            Code = Col(mapping!.ItemCode), Name = Col(mapping.ItemName), Batch = Col(mapping.Batch), Exp = Col(mapping.Expiry), Qty = Col(mapping.Qty),
            Free = Col(mapping.Free), Mrp = Col(mapping.Mrp), Rate = Col(mapping.Rate), Disc = Col(mapping.Discount), Gst = Col(mapping.Gst),
        };
        Require(c.Name >= 0 && c.Qty >= 0, "Item name / quantity column not found in the header row.");

        if (r.SaveMapping)
        {
            supplier.ImportMappingJson = JsonSerializer.Serialize(mapping, Json);
            await db.SaveChangesAsync(ct);
        }

        var maps = await db.SupplierProductMaps.AsNoTracking().Where(m => m.SupplierId == supplier.Id).ToListAsync(ct);
        var products = await db.Products.AsNoTracking().Where(p => p.IsActive).Select(p => new { p.Id, p.Name, p.PackSize, p.GstRate }).ToListAsync(ct);
        var byNorm = products.GroupBy(p => Normalize(p.Name)).ToDictionary(g => g.Key, g => g.First());

        string? Cell(string[] row, int i) => i >= 0 && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? row[i].Trim() : null;
        var rows = new List<ImportRow>();
        for (var i = 1; i < table.Count; i++)
        {
            var row = table[i];
            var name = Cell(row, c.Name);
            if (name is null) continue; // blank / totals lines
            var code = Cell(row, c.Code);
            var issues = new List<string>();
            var norm = Normalize(name);

            Guid? pid = null; string? matchedBy = null;
            var byCode = code is null ? null : maps.FirstOrDefault(m => m.SupplierItemCode == code);
            if (byCode is not null) { pid = byCode.ProductId; matchedBy = "supplier code"; }
            else if (maps.FirstOrDefault(m => m.SupplierItemName == norm) is { } byName) { pid = byName.ProductId; matchedBy = "learned name"; }
            else if (byNorm.TryGetValue(norm, out var exact)) { pid = exact.Id; matchedBy = "exact name"; }

            var suggestions = new List<ProductSuggestion>();
            if (pid is null)
            {
                var first = norm.Split(' ')[0];
                suggestions = products.Where(p => Normalize(p.Name).StartsWith(first) || Normalize(p.Name).Contains(norm))
                    .OrderBy(p => Math.Abs(Normalize(p.Name).Length - norm.Length)).Take(5)
                    .Select(p => new ProductSuggestion(p.Id, p.Name, p.PackSize)).ToList();
                issues.Add("Product not matched — choose one.");
            }
            var product = pid is null ? null : products.FirstOrDefault(p => p.Id == pid);

            var qty = ParseDecimal(Cell(row, c.Qty));
            if (qty is null or < 0) { issues.Add("Quantity missing or invalid."); qty = 0; }
            var expiry = ParseExpiry(Cell(row, c.Exp));
            if (c.Exp >= 0 && expiry is null) issues.Add("Expiry not recognised.");
            if (expiry is not null && expiry < BusinessCalendar.LocalDate(clock.UtcNow)) issues.Add("Batch already expired.");
            var batch = Cell(row, c.Batch);
            if (batch is null) issues.Add("Batch number missing.");
            var mrp = ParseDecimal(Cell(row, c.Mrp));
            if (mrp is null or <= 0) issues.Add("MRP missing.");
            var gst = ParseDecimal(Cell(row, c.Gst)?.TrimEnd('%')) ?? product?.GstRate;

            rows.Add(new ImportRow(i, code, name, pid, product?.Name, matchedBy, suggestions, batch?.ToUpperInvariant(), expiry, qty.Value,
                ParseDecimal(Cell(row, c.Free)) ?? 0, mrp, ParseDecimal(Cell(row, c.Rate)), ParseDecimal(Cell(row, c.Disc)?.TrimEnd('%')) ?? 0, gst, issues));
        }
        Require(rows.Count > 0, "No item rows found.");
        return new ImportPreview(supplier.Id, headers, mapping, rows, rows.Count(x => x.ProductId is not null), rows.Count(x => x.ProductId is null));
    }

    /// <summary>Remember confirmed supplier-name → product links so the next import matches automatically.</summary>
    public async Task<int> SaveMappingsAsync(Guid supplierId, List<ItemMapInput> items, CancellationToken ct = default)
    {
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct)) throw BusinessException.NotFound("Supplier");
        Require(items.Count <= 1000, "Too many mappings at once.");
        var productIds = items.Select(i => i.ProductId).Distinct().ToList();
        var valid = await db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Id).ToListAsync(ct);
        var existing = await db.SupplierProductMaps.Where(m => m.SupplierId == supplierId).ToListAsync(ct);
        var count = 0;
        foreach (var i in items)
        {
            if (!valid.Contains(i.ProductId) || string.IsNullOrWhiteSpace(i.SupplierItemName)) continue;
            var norm = Normalize(i.SupplierItemName);
            if (norm.Length > 200) continue;
            var m = existing.FirstOrDefault(x => x.SupplierItemName == norm);
            if (m is null) { m = new SupplierProductMap { SupplierId = supplierId, SupplierItemName = norm }; db.SupplierProductMaps.Add(m); existing.Add(m); }
            m.ProductId = i.ProductId;
            m.SupplierItemCode = string.IsNullOrWhiteSpace(i.SupplierItemCode) ? m.SupplierItemCode : i.SupplierItemCode.Trim();
            m.UpdatedAt = clock.UtcNow;
            count++;
        }
        audit.Record("supplier_map.saved", nameof(Supplier), supplierId, $"{count} item links");
        await db.SaveChangesAsync(ct);
        return count;
    }

    public static string Normalize(string s) => SpacesRx().Replace(NonWordRx().Replace(s.ToUpperInvariant(), " "), " ").Trim();

    private static ImportMapping? GuessMapping(List<string> headers)
    {
        var used = new HashSet<string>();
        // keys are tried in priority order: exact header match first, then "contains"; a header is used once
        string? Find(params string[] keys)
        {
            foreach (var k in keys)
                if (headers.FirstOrDefault(h => !used.Contains(h) && Normalize(h) == k) is { } exact) { used.Add(exact); return exact; }
            foreach (var k in keys)
                if (headers.FirstOrDefault(h => !used.Contains(h) && Normalize(h).Contains(k)) is { } partial) { used.Add(partial); return partial; }
            return null;
        }
        var code = Find("ITEM CODE", "CODE");
        var name = Find("ITEM NAME", "PRODUCT NAME", "PRODUCT", "DESCRIPTION", "ITEM", "NAME");
        var free = Find("FREE", "SCHEME", "BONUS");
        var qty = Find("QTY", "QUANTITY");
        if (name is null || qty is null) return null;
        return new ImportMapping(code, name, Find("BATCH"), Find("EXPIRY", "EXP"), qty, free,
            Find("MRP"), Find("RATE", "PTR", "PRICE"), Find("DISCOUNT", "DISC"), Find("GST", "TAX"), Find("PACK"));
    }

    private static decimal? ParseDecimal(string? s) =>
        s is not null && decimal.TryParse(s.Replace(",", "").Replace("₹", "").Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null;

    /// <summary>Accepts MM/YY, MM/YYYY, MMM-YY, MMM-YYYY, dd/MM/yyyy, yyyy-MM-dd. Always returns the month end.</summary>
    public static DateOnly? ParseExpiry(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim();
        string[] fullFormats = ["dd/MM/yyyy", "d/M/yyyy", "dd-MM-yyyy", "yyyy-MM-dd", "dd.MM.yyyy"];
        if (DateOnly.TryParseExact(s, fullFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var full))
            return BusinessCalendar.NormalizeExpiry(full);
        string[] monthFormats = ["MM/yy", "M/yy", "MM/yyyy", "M/yyyy", "MM-yy", "MM-yyyy", "MMM-yy", "MMM-yyyy", "MMM yy", "MMM yyyy", "MMMyy"];
        if (DateTime.TryParseExact(s, monthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var m))
            return BusinessCalendar.EndOfMonth(m.Year, m.Month);
        return null;
    }

    /// <summary>Minimal RFC 4180 CSV reader (quoted fields, escaped quotes, CRLF/LF).</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') inQuotes = false;
                else field.Append(ch);
            }
            else if (ch == '"') inQuotes = true;
            else if (ch == ',') { row.Add(field.ToString()); field.Clear(); }
            else if (ch is '\n' or '\r')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString()); field.Clear();
                if (row.Any(f => f.Length > 0)) rows.Add(row.ToArray());
                row = [];
            }
            else field.Append(ch);
        }
        row.Add(field.ToString());
        if (row.Any(f => f.Length > 0)) rows.Add(row.ToArray());
        return rows;
    }

    [GeneratedRegex(@"[^A-Z0-9]+")] private static partial Regex NonWordRx();
    [GeneratedRegex(@"\s+")] private static partial Regex SpacesRx();
}

/// <summary>
/// Pluggable invoice extraction (PDF/image → rows). No AI provider is configured: using one is a paid
/// third-party service that needs explicit approval. Any implementation must only return a preview for human review.
/// </summary>
public interface IInvoiceExtractor
{
    bool IsConfigured { get; }
    Task<ImportPreview> ExtractAsync(Guid supplierId, Stream file, string contentType, CancellationToken ct);
}

public sealed class NotConfiguredInvoiceExtractor : IInvoiceExtractor
{
    public bool IsConfigured => false;
    public Task<ImportPreview> ExtractAsync(Guid supplierId, Stream file, string contentType, CancellationToken ct) =>
        throw new BusinessException("ai_not_configured",
            "Automatic PDF/image reading is not set up. It needs an AI provider (a paid service) to be approved and configured. Use CSV import for now.", 501);
}
