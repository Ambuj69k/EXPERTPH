using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

// ---------- file storage ----------

public interface IFileStore
{
    Task<string> SaveAsync(Stream content, string extension, CancellationToken ct);
    Stream Open(string storagePath);
}

/// <summary>Local disk storage (configure Storage:Root on a data drive). Files get random names; nothing is served statically.</summary>
public sealed class LocalFileStore(string root) : IFileStore
{
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken ct)
    {
        var rel = Path.Combine(DateTime.UtcNow.ToString("yyyyMM"), $"{Guid.NewGuid():N}{extension}");
        var full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var f = File.Create(full);
        await content.CopyToAsync(f, ct);
        return rel;
    }

    public Stream Open(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, storagePath));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException();
        return File.OpenRead(full);
    }
}

// ---------- contracts ----------

public record StoreInfo(Guid BranchId, string BusinessName, string BranchName, string? Address, string? Phone, string? DrugLicenseNo,
    bool DeliveryAvailable, string[] DeliveryPincodes, string? Notice, decimal DeliveryCharge = 0, decimal? FreeDeliveryAbove = null);
public record PublicProduct(Guid ProductId, string Name, string? Composition, string? Manufacturer, decimal PackSize, string PackUnit, string BaseUnit,
    bool AllowLooseSale, decimal MrpPerPack, bool InStock, decimal? PricePerPack = null);
public record PublicLineInput(Guid ProductId, decimal QtyUnits);
public record PlaceOrderRequest(string Name, string Phone, FulfilmentType Fulfilment, string? Address, string? Pincode, string? RequestText,
    string? Notes, List<PublicLineInput> Lines);
public record PlacedOrder(string OrderNo, string TrackingToken, string HandoverCode);
public record TrackLine(string ProductName, decimal RequestedQtyUnits, decimal? ApprovedQtyUnits, bool AddedByPharmacist);
public record TrackView(string OrderNo, OnlineOrderStatus Status, FulfilmentType Fulfilment, string StoreName, string? StorePhone, DateTimeOffset CreatedAt,
    string? PharmacistNotes, string? RejectReason, string HandoverCode, decimal? AmountDue, int PrescriptionsUploaded, List<TrackLine> Lines);

public record ReviewLine(Guid ProductId, decimal ApprovedQtyUnits);
public record ReviewRequest(bool Accept, List<ReviewLine> Lines, string? Notes, string? PrescriberName, string? RejectReason);
public record CompleteRequest(string? HandoverCode, SettlementMode Mode, string? Reference);
public record AssignRequest(Guid AgentUserId);
public record OnlineSettingsRequest(bool Enabled, string? DeliveryPincodes, string? Notice, decimal DeliveryCharge = 0, decimal? FreeDeliveryAbove = null,
    decimal DeliveryChargeGstRate = 18m);
public record OrderFileDto(Guid Id, string FileName, string ContentType, long SizeBytes, DateTimeOffset UploadedAt);
public record OrderLineDto(Guid ProductId, string ProductName, DrugSchedule Schedule, decimal RequestedQtyUnits, decimal? ApprovedQtyUnits, bool AddedByPharmacist, decimal AvailableUnits);
public record OnlineOrderDto(Guid Id, string OrderNo, OnlineOrderStatus Status, FulfilmentType Fulfilment, string CustomerName, string? CustomerPhone,
    string? Address, string? Pincode, string? RequestText, string? CustomerNotes, string? PharmacistNotes, string? PrescriberName, string? RejectReason,
    Guid? SalesInvoiceId, string? InvoiceNo, decimal? AmountDue, Guid? DeliveryAgentId, string? DeliveryAgentName, int DeliveryAttempts,
    string? DeliveryFailureReason, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, List<OrderLineDto> Lines, List<OrderFileDto> Files);
public record CodRow(string OrderNo, string? AgentName, SettlementMode Mode, decimal Amount, DateTimeOffset At);

/// <summary>
/// Public ordering page (anonymous customers). Safeguards:
///  • only products the pharmacy marked "online" AND that are not scheduled (prescription) drugs are listed;
///  • prescription items can only be requested in free text with an uploaded prescription — a pharmacist decides;
///  • no stock is committed and no bill is made until a pharmacist reviews the order;
///  • rate-limited per phone; customers use an unguessable tracking link instead of accounts.
/// Whether a pharmacy may take online orders at all must be confirmed under current rules before enabling.
/// </summary>
public class PublicStoreService(PharmaDbContext db, IClock clock, IFileStore files)
{
    private static readonly DrugSchedule[] Listable = [DrugSchedule.None];
    private const int MaxOrdersPerPhonePerDay = 5;

    private async Task<(Branch Branch, Tenant Tenant)> OpenStoreAsync(Guid branchId, CancellationToken ct)
    {
        var b = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x => x.Id == branchId && x.IsActive && x.OnlineOrdersEnabled && !x.IsWarehouse, ct);
        var t = b is null ? null : await db.Tenants.AsNoTracking().SingleOrDefaultAsync(x => x.Id == b.TenantId && x.IsActive && x.DeletionScheduledAt == null, ct);
        var s = t is null ? null : await db.Subscriptions.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == t.Id, ct);
        var open = s is not null && SubscriptionService.ModulesFor(s).Contains(Plans.Online) && s.Status != SubscriptionStatus.Cancelled
                   && !(s.Status == SubscriptionStatus.Trial && s.TrialEndsAt < clock.UtcNow);
        if (!open) throw BusinessException.NotFound("Store");
        return (b!, t!);
    }

    public async Task<StoreInfo> StoreAsync(Guid branchId, CancellationToken ct = default)
    {
        var (b, t) = await OpenStoreAsync(branchId, ct);
        var pins = Pincodes(b);
        return new StoreInfo(b.Id, t.Name, b.Name, b.Address, b.Phone, b.DrugLicenseNo, pins.Length > 0, pins, b.OnlineNotice, b.DeliveryCharge, b.FreeDeliveryAbove);
    }

    public async Task<List<PublicProduct>> CatalogAsync(Guid branchId, string? q, CancellationToken ct = default)
    {
        var (b, _) = await OpenStoreAsync(branchId, ct);
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var products = db.Products.IgnoreQueryFilters().AsNoTracking().Where(p => p.TenantId == b.TenantId && p.IsActive && p.OnlineListed && Listable.Contains(p.Schedule));
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => p.Name.Contains(q.Trim()) || (p.Composition != null && p.Composition.Contains(q.Trim())));
        var list = await products.OrderBy(p => p.Name).Take(100).ToListAsync(ct);
        var ids = list.Select(p => p.Id).ToList();
        var stock = await db.StockBalances.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.TenantId == b.TenantId && s.BranchId == b.Id && ids.Contains(s.ProductId) && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .OrderBy(s => s.Batch!.ExpiryDate).Select(s => new { s.ProductId, s.Batch!.Mrp }).ToListAsync(ct);
        var prices = await db.BranchProductPrices.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == b.TenantId && x.BranchId == b.Id && ids.Contains(x.ProductId))
            .ToDictionaryAsync(x => x.ProductId, x => x.PricePerPack, ct);
        return list.Select(p =>
        {
            var first = stock.FirstOrDefault(s => s.ProductId == p.Id);
            var mrp = first?.Mrp ?? 0;
            decimal? price = prices.TryGetValue(p.Id, out var pp) && pp < mrp ? pp : null;
            return new PublicProduct(p.Id, p.Name, p.Composition, p.Manufacturer, p.PackSize, p.PackUnit, p.BaseUnit, p.AllowLooseSale, mrp, first is not null, price);
        }).ToList();
    }

    public async Task<PlacedOrder> PlaceOrderAsync(Guid branchId, PlaceOrderRequest r, CancellationToken ct = default)
    {
        var (b, _) = await OpenStoreAsync(branchId, ct);
        Require(!string.IsNullOrWhiteSpace(r.Name) && r.Name.Length <= 100, "Your name is required.");
        var phone = MasterDataService.NormalizePhone(r.Phone) ?? throw BusinessException.Validation("Mobile number is required.");
        Require(Enum.IsDefined(r.Fulfilment), "Choose pickup or delivery.");
        Require((r.Lines?.Count ?? 0) > 0 || !string.IsNullOrWhiteSpace(r.RequestText), "Add items, or describe what you need from your prescription.");
        Require((r.Lines?.Count ?? 0) <= 50 && (r.RequestText?.Length ?? 0) <= 1000, "Order is too large.");
        if (r.Fulfilment == FulfilmentType.Delivery)
        {
            Require(!string.IsNullOrWhiteSpace(r.Address) && r.Address.Length <= 500, "Delivery address is required.");
            Require(Pincodes(b).Contains(r.Pincode?.Trim()), "Sorry, this store doesn't deliver to that pincode. Choose pickup instead.");
        }
        var since = clock.UtcNow.AddDays(-1);
        if (await db.OnlineOrders.IgnoreQueryFilters().CountAsync(o => o.TenantId == b.TenantId && o.CustomerPhone == phone && o.CreatedAt >= since, ct) >= MaxOrdersPerPhonePerDay)
            throw new BusinessException("rate_limited", "Too many orders from this number today. Please call the store.", 429);

        var ids = (r.Lines ?? []).Select(l => l.ProductId).Distinct().ToList();
        var allowed = await db.Products.IgnoreQueryFilters().Where(p => p.TenantId == b.TenantId && ids.Contains(p.Id) && p.IsActive && p.OnlineListed && Listable.Contains(p.Schedule))
            .ToDictionaryAsync(p => p.Id, ct);
        foreach (var l in r.Lines ?? [])
        {
            if (!allowed.TryGetValue(l.ProductId, out var p)) throw BusinessException.Validation("One of the items can't be ordered online.");
            Require(l.QtyUnits > 0 && l.QtyUnits <= 1000 && l.QtyUnits == decimal.Truncate(l.QtyUnits), "Invalid quantity.");
            if (!p.AllowLooseSale) Require(l.QtyUnits % p.PackSize == 0, $"{p.Name} is sold in full {p.PackUnit}s only.");
        }

        var now = clock.UtcNow;
        var order = new OnlineOrder
        {
            TenantId = b.TenantId, BranchId = b.Id, CustomerName = r.Name.Trim(), CustomerPhone = phone, Fulfilment = r.Fulfilment,
            Address = r.Fulfilment == FulfilmentType.Delivery ? r.Address!.Trim() : null, Pincode = r.Fulfilment == FulfilmentType.Delivery ? r.Pincode!.Trim() : null,
            RequestText = r.RequestText?.Trim(), CustomerNotes = r.Notes?.Trim(), CreatedAt = now, UpdatedAt = now,
            TrackingToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant(),
            HandoverCode = RandomNumberGenerator.GetInt32(1000, 10000).ToString(),
            OrderNo = $"W{b.Code}-{now:MMdd}-{RandomNumberGenerator.GetInt32(1000, 10000)}",
        };
        order.Lines = (r.Lines ?? []).GroupBy(l => l.ProductId)
            .Select(g => new OnlineOrderLine { TenantId = b.TenantId, OnlineOrderId = order.Id, ProductId = g.Key, RequestedQtyUnits = g.Sum(x => x.QtyUnits) }).ToList();
        db.OnlineOrders.Add(order);
        db.AuditEvents.Add(new AuditEvent { TenantId = b.TenantId, Action = "online_order.placed", EntityType = nameof(OnlineOrder), EntityId = order.Id, Summary = order.OrderNo, At = now });
        await db.SaveChangesAsync(ct);
        return new PlacedOrder(order.OrderNo, order.TrackingToken, order.HandoverCode);
    }

    private async Task<OnlineOrder> ByToken(string token, CancellationToken ct) =>
        await db.OnlineOrders.IgnoreQueryFilters().Include(o => o.Lines).ThenInclude(l => l.Product).Include(o => o.Files)
            .SingleOrDefaultAsync(o => o.TrackingToken == token, ct) ?? throw BusinessException.NotFound("Order");

    public async Task<TrackView> TrackAsync(string token, CancellationToken ct = default)
    {
        var o = await ByToken(token, ct);
        var b = await db.Branches.IgnoreQueryFilters().AsNoTracking().SingleAsync(x => x.Id == o.BranchId, ct);
        var due = o.SalesInvoiceId is null ? (decimal?)null
            : await db.SalesInvoices.IgnoreQueryFilters().Where(s => s.Id == o.SalesInvoiceId).Select(s => s.NetAmount).SingleAsync(ct);
        return new TrackView(o.OrderNo, o.Status, o.Fulfilment, b.Name, b.Phone, o.CreatedAt, o.PharmacistNotes, o.RejectReason, o.HandoverCode, due, o.Files.Count,
            o.Lines.Select(l => new TrackLine(l.Product!.Name, l.RequestedQtyUnits, l.ApprovedQtyUnits, l.AddedByPharmacist)).ToList());
    }

    /// <summary>Accepts PDF/JPEG/PNG up to 5 MB (checked by file signature, not by the declared type). Max 3 per order.</summary>
    public async Task<TrackView> UploadPrescriptionAsync(string token, Stream content, string fileName, CancellationToken ct = default)
    {
        var o = await ByToken(token, ct);
        Require(o.Status is OnlineOrderStatus.Submitted or OnlineOrderStatus.NeedsCustomerApproval, "Prescriptions can only be added before the order is accepted.");
        Require(o.Files.Count < 3, "Up to 3 files per order.");
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, ct);
        Require(ms.Length is > 0 and <= 5 * 1024 * 1024, "File must be under 5 MB.");
        var bytes = ms.GetBuffer();
        var (type, ext) = bytes switch
        {
            [0x25, 0x50, 0x44, 0x46, ..] => ("application/pdf", ".pdf"),
            [0xFF, 0xD8, 0xFF, ..] => ("image/jpeg", ".jpg"),
            [0x89, 0x50, 0x4E, 0x47, ..] => ("image/png", ".png"),
            _ => (null, null),
        };
        Require(type is not null, "Upload a PDF, JPG or PNG.");
        ms.Position = 0;
        var path = await files.SaveAsync(ms, ext!, ct);
        db.OnlineOrderFiles.Add(new OnlineOrderFile
        {
            TenantId = o.TenantId, OnlineOrderId = o.Id, FileName = Path.GetFileName(fileName ?? "prescription")[..Math.Min(100, Path.GetFileName(fileName ?? "prescription").Length)],
            ContentType = type!, SizeBytes = ms.Length, StoragePath = path, UploadedAt = clock.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return await TrackAsync(token, ct);
    }

    public async Task<TrackView> CustomerDecisionAsync(string token, bool approve, CancellationToken ct = default)
    {
        var o = await ByToken(token, ct);
        if (approve)
        {
            Require(o.Status == OnlineOrderStatus.NeedsCustomerApproval, "Nothing to approve.");
            o.Status = OnlineOrderStatus.Accepted;
        }
        else
        {
            Require(o.Status is OnlineOrderStatus.Submitted or OnlineOrderStatus.NeedsCustomerApproval or OnlineOrderStatus.Accepted,
                "This order is already being packed — please call the store to cancel.");
            o.Status = OnlineOrderStatus.Cancelled;
        }
        o.UpdatedAt = clock.UtcNow;
        db.AuditEvents.Add(new AuditEvent { TenantId = o.TenantId, Action = approve ? "online_order.customer_approved" : "online_order.customer_cancelled", EntityType = nameof(OnlineOrder), EntityId = o.Id, At = clock.UtcNow });
        await db.SaveChangesAsync(ct);
        return await TrackAsync(token, ct);
    }

    public static string[] Pincodes(Branch b) =>
        (b.DeliveryPincodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => p.Length == 6 && p.All(char.IsDigit)).ToArray();
}

/// <summary>Store-side handling of online orders: pharmacist review, packing (billing), handover, delivery and cancellation.</summary>
public class OnlineOrderService(PharmaDbContext db, ITenantContext ctx, IClock clock, SalesService sales, LedgerService ledger, AuditService audit, IFileStore files)
{
    private static readonly DrugSchedule[] NeedsPrescription = [DrugSchedule.H, DrugSchedule.H1, DrugSchedule.X, DrugSchedule.NarcoticPsychotropic];

    public async Task SaveSettingsAsync(Guid branchId, OnlineSettingsRequest r, CancellationToken ct = default)
    {
        var b = await db.Branches.SingleOrDefaultAsync(x => x.Id == branchId, ct) ?? throw BusinessException.NotFound("Branch");
        var pins = (r.DeliveryPincodes ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        Require(pins.All(p => p.Length == 6 && p.All(char.IsDigit)), "Pincodes must be 6 digits, separated by commas.");
        Require(!r.Enabled || !b.IsWarehouse, "Warehouses can't take online orders.");
        b.OnlineOrdersEnabled = r.Enabled;
        b.DeliveryPincodes = string.Join(",", pins.Distinct());
        b.OnlineNotice = r.Notice?.Trim();
        Require(r.DeliveryCharge is >= 0 and <= 5000, "Delivery charge must be 0-5000.");
        Require(r.FreeDeliveryAbove is null or > 0, "Free-delivery threshold must be positive.");
        Require(new[] { 0m, 5m, 12m, 18m, 28m }.Contains(r.DeliveryChargeGstRate), "GST rate must be 0, 5, 12, 18 or 28.");
        b.DeliveryCharge = Money.R2(r.DeliveryCharge);
        b.FreeDeliveryAbove = r.FreeDeliveryAbove;
        b.DeliveryChargeGstRate = r.DeliveryChargeGstRate;
        audit.Record("online.settings", nameof(Branch), b.Id, $"enabled {r.Enabled}; {pins.Count} pincodes");
        await db.SaveChangesAsync(ct);
    }

    private async Task<OnlineOrder> Load(Guid id, CancellationToken ct) =>
        await db.OnlineOrders.Include(o => o.Lines).ThenInclude(l => l.Product).Include(o => o.Files)
            .SingleOrDefaultAsync(o => o.Id == id && o.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Order");

    public async Task<List<OnlineOrderDto>> ListAsync(OnlineOrderStatus? status, CancellationToken ct = default)
    {
        var q = db.OnlineOrders.AsNoTracking().Where(o => o.BranchId == ctx.BranchId);
        if (status is not null) q = q.Where(o => o.Status == status);
        var ids = await q.OrderByDescending(o => o.UpdatedAt).Take(200).Select(o => o.Id).ToListAsync(ct);
        var list = new List<OnlineOrderDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    public async Task<OnlineOrderDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var o = await db.OnlineOrders.AsNoTracking().Include(x => x.Lines).ThenInclude(l => l.Product).Include(x => x.Files)
                    .SingleOrDefaultAsync(x => x.Id == id && x.BranchId == ctx.BranchId, ct) ?? throw BusinessException.NotFound("Order");
        var isAgent = ctx.Role == UserRole.DeliveryAgent;
        if (isAgent && o.DeliveryAgentId != ctx.UserId) throw BusinessException.NotFound("Order");
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var ids = o.Lines.Select(l => l.ProductId).ToList();
        var avail = await db.StockBalances.Where(s => s.BranchId == o.BranchId && ids.Contains(s.ProductId) && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today && !s.Batch.IsBlocked)
            .GroupBy(s => s.ProductId).Select(g => new { g.Key, Q = g.Sum(x => x.QtyUnits) }).ToDictionaryAsync(x => x.Key, x => x.Q, ct);
        var inv = o.SalesInvoiceId is null ? null : await db.SalesInvoices.Where(s => s.Id == o.SalesInvoiceId).Select(s => new { s.InvoiceNo, s.NetAmount }).SingleAsync(ct);
        var agent = o.DeliveryAgentId is null ? null : await db.Users.Where(u => u.Id == o.DeliveryAgentId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct);
        // delivery agents see the customer's phone only while the delivery is active (privacy)
        var showPhone = !isAgent || o.Status == OnlineOrderStatus.OutForDelivery;
        return new OnlineOrderDto(o.Id, o.OrderNo, o.Status, o.Fulfilment, o.CustomerName, showPhone ? o.CustomerPhone : null, o.Address, o.Pincode,
            isAgent ? null : o.RequestText, o.CustomerNotes, isAgent ? null : o.PharmacistNotes, isAgent ? null : o.PrescriberName, o.RejectReason,
            o.SalesInvoiceId, inv?.InvoiceNo, inv?.NetAmount, o.DeliveryAgentId, agent, o.DeliveryAttempts, o.DeliveryFailureReason, o.CreatedAt, o.UpdatedAt,
            isAgent ? [] : o.Lines.Select(l => new OrderLineDto(l.ProductId, l.Product!.Name, l.Product.Schedule, l.RequestedQtyUnits, l.ApprovedQtyUnits, l.AddedByPharmacist,
                avail.GetValueOrDefault(l.ProductId))).ToList(),
            isAgent ? [] : o.Files.Select(f => new OrderFileDto(f.Id, f.FileName, f.ContentType, f.SizeBytes, f.UploadedAt)).ToList());
    }

    /// <summary>
    /// Pharmacist decision. Lines may be reduced/removed and items added from a prescription request.
    /// Any change from what the customer asked for needs the customer's approval on the tracking page.
    /// Scheduled drugs require an uploaded prescription and the prescriber's name.
    /// </summary>
    public async Task<OnlineOrderDto> ReviewAsync(Guid id, ReviewRequest r, CancellationToken ct = default)
    {
        var o = await Load(id, ct);
        Require(o.Status == OnlineOrderStatus.Submitted, "Only new orders can be reviewed.");
        o.ReviewedByUserId = ctx.UserId;
        o.PharmacistNotes = r.Notes?.Trim();
        o.UpdatedAt = clock.UtcNow;
        if (!r.Accept)
        {
            Require(!string.IsNullOrWhiteSpace(r.RejectReason), "Give the customer a reason.");
            o.Status = OnlineOrderStatus.Rejected;
            o.RejectReason = r.RejectReason!.Trim();
            audit.Record("online_order.rejected", nameof(OnlineOrder), o.Id, r.RejectReason);
            await db.SaveChangesAsync(ct);
            return await GetAsync(id, ct);
        }

        var changed = false;
        foreach (var rl in r.Lines)
        {
            Require(rl.ApprovedQtyUnits >= 0 && rl.ApprovedQtyUnits == decimal.Truncate(rl.ApprovedQtyUnits), "Quantities must be whole numbers ≥ 0.");
            var line = o.Lines.SingleOrDefault(l => l.ProductId == rl.ProductId);
            if (line is null)
            {
                var p = await db.Products.SingleOrDefaultAsync(x => x.Id == rl.ProductId && x.IsActive, ct) ?? throw BusinessException.Validation("Unknown product.");
                if (rl.ApprovedQtyUnits == 0) continue;
                line = new OnlineOrderLine { OnlineOrderId = o.Id, ProductId = p.Id, Product = p, RequestedQtyUnits = 0, AddedByPharmacist = true };
                o.Lines.Add(line);
                changed = true;
            }
            line.ApprovedQtyUnits = rl.ApprovedQtyUnits;
            if (rl.ApprovedQtyUnits != line.RequestedQtyUnits) changed = true;
        }
        foreach (var l in o.Lines.Where(l => l.ApprovedQtyUnits is null)) { l.ApprovedQtyUnits = 0; changed = true; }
        Require(o.Lines.Any(l => l.ApprovedQtyUnits > 0), "Approve at least one item, or reject the order.");

        if (o.Lines.Any(l => l.ApprovedQtyUnits > 0 && NeedsPrescription.Contains(l.Product!.Schedule)))
        {
            Require(o.Files.Count > 0, "This order includes prescription-only medicine: the customer must upload a valid prescription first.");
            Require(!string.IsNullOrWhiteSpace(r.PrescriberName), "Record the prescriber's name from the prescription.");
            o.PrescriberName = r.PrescriberName!.Trim();
        }
        o.Status = changed ? OnlineOrderStatus.NeedsCustomerApproval : OnlineOrderStatus.Accepted;
        audit.Record("online_order.reviewed", nameof(OnlineOrder), o.Id, o.Status.ToString());
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Bills the approved items (stock leaves now, FEFO). The amount is booked as the customer's due until payment at handover.</summary>
    public async Task<OnlineOrderDto> PackAsync(Guid id, CancellationToken ct = default)
    {
        var o = await Load(id, ct);
        Require(o.Status == OnlineOrderStatus.Accepted, "Only accepted orders can be packed.");
        var lines = o.Lines.Where(l => l.ApprovedQtyUnits > 0).ToList();
        var saleLines = lines.Select(l => new SaleLineInput(l.ProductId, null, l.ApprovedQtyUnits!.Value, 0)).ToList();
        var itemsTotal = await sales.PreviewTotalAsync(saleLines, ct);
        var branch = await db.Branches.AsNoTracking().SingleAsync(b => b.Id == o.BranchId, ct);
        var delivery = o.Fulfilment == FulfilmentType.Delivery && !(branch.FreeDeliveryAbove is { } free && itemsTotal >= free) ? branch.DeliveryCharge : 0m;
        var preview = await sales.PreviewTotalAsync(saleLines, ct, delivery);
        var sale = await sales.CreateAsync(new SaleRequest(o.Id, // the order id is the idempotency key: packing twice can't bill twice
            new CustomerInput(o.CustomerName, o.CustomerPhone), o.PrescriberName, saleLines,
            [new PaymentInput(PaymentMode.Credit, preview, $"Online {o.OrderNo}")], DeliveryCharge: delivery), ct);
        db.ChangeTracker.Clear();
        o = await Load(id, ct);
        o.SalesInvoiceId = sale.Id;
        o.CustomerId = sale.CustomerId;
        o.Status = o.Fulfilment == FulfilmentType.Pickup ? OnlineOrderStatus.ReadyForPickup : OnlineOrderStatus.Packed;
        o.UpdatedAt = clock.UtcNow;
        audit.Record("online_order.packed", nameof(OnlineOrder), o.Id, sale.InvoiceNo);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<OnlineOrderDto> AssignAsync(Guid id, Guid agentUserId, CancellationToken ct = default)
    {
        var o = await Load(id, ct);
        Require(o.Fulfilment == FulfilmentType.Delivery && o.Status is OnlineOrderStatus.Packed or OnlineOrderStatus.DeliveryFailed, "Order isn't ready for dispatch.");
        var agent = await db.Users.SingleOrDefaultAsync(u => u.Id == agentUserId && u.IsActive && u.Role == UserRole.DeliveryAgent && u.DefaultBranchId == o.BranchId, ct)
                    ?? throw BusinessException.Validation("Choose an active delivery agent of this branch.");
        o.DeliveryAgentId = agent.Id;
        o.DeliveryAttempts++;
        o.Status = OnlineOrderStatus.OutForDelivery;
        o.UpdatedAt = clock.UtcNow;
        audit.Record("online_order.dispatched", nameof(OnlineOrder), o.Id, agent.DisplayName);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Handover: delivery requires the customer's code; the payment collected settles the bill (cash on delivery reconciles to the agent).</summary>
    public async Task<OnlineOrderDto> CompleteAsync(Guid id, CompleteRequest r, CancellationToken ct = default)
    {
        var o = await Load(id, ct);
        var pickup = o.Fulfilment == FulfilmentType.Pickup && o.Status == OnlineOrderStatus.ReadyForPickup;
        var delivery = o.Fulfilment == FulfilmentType.Delivery && o.Status == OnlineOrderStatus.OutForDelivery;
        Require(pickup || delivery, "Order isn't at handover stage.");
        if (ctx.Role == UserRole.DeliveryAgent) Require(delivery && o.DeliveryAgentId == ctx.UserId, "This delivery isn't assigned to you.");
        if (delivery) Require(r.HandoverCode?.Trim() == o.HandoverCode, "Handover code doesn't match. Ask the customer for the code on their order page.");
        Require(r.Mode is SettlementMode.Cash or SettlementMode.Upi or SettlementMode.Card, "Collect payment by cash, UPI or card.");
        var due = await db.SalesInvoices.Where(s => s.Id == o.SalesInvoiceId).Select(s => s.NetAmount).SingleAsync(ct);
        await ledger.CreateCustomerReceiptAsync(new CustomerReceiptRequest(o.CustomerId!.Value, BusinessCalendar.LocalDate(clock.UtcNow), r.Mode, due,
            string.IsNullOrWhiteSpace(r.Reference) ? $"Online {o.OrderNo}" : r.Reference.Trim(), [new AllocationInput(o.SalesInvoiceId!.Value, due)]), ct);
        db.ChangeTracker.Clear();
        o = await Load(id, ct);
        o.Status = OnlineOrderStatus.Completed;
        o.UpdatedAt = clock.UtcNow;
        audit.Record("online_order.completed", nameof(OnlineOrder), o.Id, $"{r.Mode} {due}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<OnlineOrderDto> FailDeliveryAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        var o = await Load(id, ct);
        Require(o.Status == OnlineOrderStatus.OutForDelivery, "Order isn't out for delivery.");
        if (ctx.Role == UserRole.DeliveryAgent) Require(o.DeliveryAgentId == ctx.UserId, "This delivery isn't assigned to you.");
        o.Status = OnlineOrderStatus.DeliveryFailed;
        o.DeliveryFailureReason = reason.Trim();
        o.UpdatedAt = clock.UtcNow;
        audit.Record("online_order.delivery_failed", nameof(OnlineOrder), o.Id, reason);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Store cancels: if already billed, the bill is cancelled and stock returns to the same batches.</summary>
    public async Task<OnlineOrderDto> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        Require(!string.IsNullOrWhiteSpace(reason), "Reason is required.");
        var o = await Load(id, ct);
        Require(o.Status is not (OnlineOrderStatus.Completed or OnlineOrderStatus.Cancelled or OnlineOrderStatus.Rejected), "Order is already closed.");
        Require(o.Status != OnlineOrderStatus.OutForDelivery, "Mark the delivery as failed first.");
        if (o.SalesInvoiceId is { } invoiceId)
        {
            await sales.CancelAsync(invoiceId, $"Online order {o.OrderNo} cancelled: {reason}", ct);
            db.ChangeTracker.Clear();
            o = await Load(id, ct);
        }
        o.Status = OnlineOrderStatus.Cancelled;
        o.RejectReason = reason.Trim();
        o.UpdatedAt = clock.UtcNow;
        audit.Record("online_order.cancelled", nameof(OnlineOrder), o.Id, reason);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<(Stream Stream, string ContentType, string FileName)> FileAsync(Guid orderId, Guid fileId, CancellationToken ct = default)
    {
        var f = await db.OnlineOrderFiles.SingleOrDefaultAsync(x => x.Id == fileId && x.OnlineOrderId == orderId
                                                                   && db.OnlineOrders.Any(o => o.Id == orderId && o.BranchId == ctx.BranchId), ct)
                ?? throw BusinessException.NotFound("File");
        audit.Record("online_order.prescription_viewed", nameof(OnlineOrder), orderId, f.FileName); // sensitive access is logged
        await db.SaveChangesAsync(ct);
        return (files.Open(f.StoragePath), f.ContentType, f.FileName);
    }

    public async Task<List<UserDto>> AgentsAsync(CancellationToken ct = default) =>
        await db.Users.Where(u => u.Role == UserRole.DeliveryAgent && u.IsActive && u.DefaultBranchId == ctx.BranchId).OrderBy(u => u.DisplayName)
            .Select(u => new UserDto(u.Id, u.Email, u.DisplayName, u.Role, u.DefaultBranchId, u.IsActive)).ToListAsync(ct);

    public async Task<List<OnlineOrderDto>> MyDeliveriesAsync(CancellationToken ct = default)
    {
        var ids = await db.OnlineOrders.Where(o => o.DeliveryAgentId == ctx.UserId && o.Status == OnlineOrderStatus.OutForDelivery)
            .OrderBy(o => o.UpdatedAt).Select(o => o.Id).ToListAsync(ct);
        var list = new List<OnlineOrderDto>();
        foreach (var id in ids) list.Add(await GetAsync(id, ct));
        return list;
    }

    /// <summary>Money collected at handover for online orders on a date, per collector — for reconciling cash on delivery.</summary>
    public async Task<List<CodRow>> CollectionsAsync(DateOnly date, CancellationToken ct = default)
    {
        var orders = await db.OnlineOrders.AsNoTracking().Where(o => o.BranchId == ctx.BranchId && o.Status == OnlineOrderStatus.Completed && o.SalesInvoiceId != null)
            .Select(o => new { o.OrderNo, o.SalesInvoiceId }).ToListAsync(ct);
        var invIds = orders.Select(o => o.SalesInvoiceId!.Value).ToList();
        var receipts = await db.CustomerReceiptAllocations.AsNoTracking().Where(a => invIds.Contains(a.SalesInvoiceId))
            .Join(db.CustomerReceipts.Where(r => r.Status == DocStatus.Posted && r.ReceiptDate == date), a => a.CustomerReceiptId, r => r.Id,
                (a, r) => new { a.SalesInvoiceId, r.Mode, a.Amount, r.CreatedAt, r.CreatedByUserId }).ToListAsync(ct);
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);
        return receipts.Select(r => new CodRow(orders.First(o => o.SalesInvoiceId == r.SalesInvoiceId).OrderNo,
            r.CreatedByUserId is { } u && users.TryGetValue(u, out var n) ? n : null, r.Mode, r.Amount, r.CreatedAt)).OrderBy(x => x.At).ToList();
    }
}
