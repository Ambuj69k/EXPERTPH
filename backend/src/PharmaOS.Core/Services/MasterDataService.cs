using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record ProductInput(string Name, string? Composition, string? Manufacturer, string? HsnCode, decimal GstRate,
    decimal PackSize, string PackUnit, string BaseUnit, bool AllowLooseSale, DrugSchedule Schedule, string? Barcode,
    decimal ReorderLevelUnits, bool IsActive, int? Version, bool OnlineListed = false);

public record ProductDto(Guid Id, string Name, string? Composition, string? Manufacturer, string? HsnCode, decimal GstRate,
    decimal PackSize, string PackUnit, string BaseUnit, bool AllowLooseSale, DrugSchedule Schedule, string? Barcode,
    decimal ReorderLevelUnits, bool IsActive, int Version, bool OnlineListed = false);

public record SupplierInput(string Name, string? Gstin, string? Phone, string? Email, string? Address,
    string? DrugLicenseNo, int PaymentTermsDays, bool IsActive, int? Version);

public record SupplierDto(Guid Id, string Name, string? Gstin, string? Phone, string? Email, string? Address,
    string? DrugLicenseNo, int PaymentTermsDays, bool IsActive, int Version);

public record CustomerDto(Guid Id, string Name, string? Phone, decimal? CreditLimit);

public record CustomerInput(string Name, string? Phone, decimal? CreditLimit = null);

public class MasterDataService(PharmaDbContext db, AuditService audit, IClock clock)
{
    private static readonly decimal[] AllowedGstRates = [0m, 5m, 12m, 18m, 28m];

    // ---------- Products ----------

    public async Task<PagedResult<ProductDto>> SearchProductsAsync(string? q, bool includeInactive, int page, int pageSize,
        CancellationToken ct = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var query = db.Products.AsNoTracking();
        if (!includeInactive) query = query.Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim();
            query = query.Where(p => p.Name.Contains(s) || (p.Composition != null && p.Composition.Contains(s))
                                     || p.Barcode == s || (p.Manufacturer != null && p.Manufacturer.Contains(s)));
        }
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(p => ToDto(p)).ToListAsync(ct);
        return new PagedResult<ProductDto>(items, total, page, pageSize);
    }

    public async Task<ProductDto> GetProductAsync(Guid id, CancellationToken ct = default)
        => ToDto(await db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct)
                 ?? throw BusinessException.NotFound("Product"));

    public async Task<ProductDto> CreateProductAsync(ProductInput input, CancellationToken ct = default)
    {
        ValidateProduct(input);
        await EnsureNoDuplicateProductAsync(input, null, ct);
        var p = new Product();
        Apply(p, input);
        db.Products.Add(p);
        audit.Record("product.created", nameof(Product), p.Id, p.Name);
        await db.SaveChangesAsync(ct);
        return ToDto(p);
    }

    public async Task<ProductDto> UpdateProductAsync(Guid id, ProductInput input, CancellationToken ct = default)
    {
        ValidateProduct(input);
        var p = await db.Products.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Product");
        if (input.Version is null) throw BusinessException.Validation("Version is required for updates.");
        db.Entry(p).Property(x => x.Version).OriginalValue = input.Version.Value;

        if (p.PackSize != input.PackSize && await db.StockMovements.AnyAsync(m => m.ProductId == id, ct))
            throw BusinessException.Validation("Pack size cannot change after stock movements exist. Create a new product instead.");
        await EnsureNoDuplicateProductAsync(input, id, ct);

        Apply(p, input);
        audit.Record("product.updated", nameof(Product), p.Id, p.Name);
        await SaveWithConcurrencyAsync(ct);
        return ToDto(p);
    }

    private async Task EnsureNoDuplicateProductAsync(ProductInput i, Guid? exceptId, CancellationToken ct)
    {
        var name = i.Name.Trim();
        var dup = await db.Products.AnyAsync(p => p.Id != exceptId && p.Name == name
                                                 && p.Manufacturer == (i.Manufacturer == null ? null : i.Manufacturer.Trim())
                                                 && p.PackSize == i.PackSize, ct);
        if (dup) throw BusinessException.Conflict("duplicate_product", $"A product '{name}' with the same manufacturer and pack size already exists.");
        if (!string.IsNullOrWhiteSpace(i.Barcode) &&
            await db.Products.AnyAsync(p => p.Id != exceptId && p.Barcode == i.Barcode.Trim(), ct))
            throw BusinessException.Conflict("duplicate_barcode", "This barcode is already mapped to another product.");
    }

    private static void ValidateProduct(ProductInput i)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name) && i.Name.Length <= 200, "Product name is required (max 200).");
        Require(AllowedGstRates.Contains(i.GstRate), "GST rate must be one of 0, 5, 12, 18, 28 (verify current rates).");
        Require(i.PackSize > 0 && i.PackSize <= 10000 && decimal.Truncate(i.PackSize) == i.PackSize, "Pack size must be a positive whole number.");
        Require(!string.IsNullOrWhiteSpace(i.PackUnit) && !string.IsNullOrWhiteSpace(i.BaseUnit), "Pack and base units are required.");
        Require(i.HsnCode is null or "" || (i.HsnCode.Length is >= 4 and <= 8 && i.HsnCode.All(char.IsDigit)), "HSN must be 4-8 digits.");
        Require(i.ReorderLevelUnits >= 0, "Reorder level cannot be negative.");
        Require(Enum.IsDefined(i.Schedule), "Invalid schedule.");
    }

    private static void Apply(Product p, ProductInput i)
    {
        p.Name = i.Name.Trim();
        p.Composition = Clean(i.Composition);
        p.Manufacturer = Clean(i.Manufacturer);
        p.HsnCode = Clean(i.HsnCode);
        p.GstRate = i.GstRate;
        p.PackSize = i.PackSize;
        p.PackUnit = i.PackUnit.Trim();
        p.BaseUnit = i.BaseUnit.Trim();
        p.AllowLooseSale = i.AllowLooseSale;
        p.Schedule = i.Schedule;
        p.Barcode = Clean(i.Barcode);
        p.ReorderLevelUnits = i.ReorderLevelUnits;
        p.IsActive = i.IsActive;
        // scheduled (prescription) drugs are never listed publicly, whatever the input says
        p.OnlineListed = i.OnlineListed && i.Schedule == DrugSchedule.None;
    }

    private static ProductDto ToDto(Product p) => new(p.Id, p.Name, p.Composition, p.Manufacturer, p.HsnCode, p.GstRate,
        p.PackSize, p.PackUnit, p.BaseUnit, p.AllowLooseSale, p.Schedule, p.Barcode, p.ReorderLevelUnits, p.IsActive, p.Version, p.OnlineListed);

    // ---------- Suppliers ----------

    public async Task<PagedResult<SupplierDto>> SearchSuppliersAsync(string? q, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = NormalizePaging(page, pageSize);
        var query = db.Suppliers.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(s => s.Name.Contains(q.Trim()) || s.Gstin == q.Trim());
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(s => s.Name).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => ToDto(s)).ToListAsync(ct);
        return new PagedResult<SupplierDto>(items, total, page, pageSize);
    }

    public async Task<SupplierDto> CreateSupplierAsync(SupplierInput input, CancellationToken ct = default)
    {
        ValidateSupplier(input);
        if (await db.Suppliers.AnyAsync(s => s.Name == input.Name.Trim(), ct))
            throw BusinessException.Conflict("duplicate_supplier", "A supplier with this name already exists.");
        var s = new Supplier();
        Apply(s, input);
        db.Suppliers.Add(s);
        audit.Record("supplier.created", nameof(Supplier), s.Id, s.Name);
        await db.SaveChangesAsync(ct);
        return ToDto(s);
    }

    public async Task<SupplierDto> UpdateSupplierAsync(Guid id, SupplierInput input, CancellationToken ct = default)
    {
        ValidateSupplier(input);
        var s = await db.Suppliers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Supplier");
        if (input.Version is null) throw BusinessException.Validation("Version is required for updates.");
        db.Entry(s).Property(x => x.Version).OriginalValue = input.Version.Value;
        Apply(s, input);
        audit.Record("supplier.updated", nameof(Supplier), s.Id, s.Name);
        await SaveWithConcurrencyAsync(ct);
        return ToDto(s);
    }

    private static void ValidateSupplier(SupplierInput i)
    {
        Require(!string.IsNullOrWhiteSpace(i.Name) && i.Name.Length <= 200, "Supplier name is required.");
        Require(i.Gstin is null or "" || i.Gstin.Trim().Length == 15, "GSTIN must be 15 characters.");
        Require(i.PaymentTermsDays is >= 0 and <= 365, "Payment terms must be 0-365 days.");
    }

    private static void Apply(Supplier s, SupplierInput i)
    {
        s.Name = i.Name.Trim();
        s.Gstin = Clean(i.Gstin)?.ToUpperInvariant();
        s.Phone = Clean(i.Phone);
        s.Email = Clean(i.Email);
        s.Address = Clean(i.Address);
        s.DrugLicenseNo = Clean(i.DrugLicenseNo);
        s.PaymentTermsDays = i.PaymentTermsDays;
        s.IsActive = i.IsActive;
    }

    private static SupplierDto ToDto(Supplier s) => new(s.Id, s.Name, s.Gstin, s.Phone, s.Email, s.Address,
        s.DrugLicenseNo, s.PaymentTermsDays, s.IsActive, s.Version);

    // ---------- Customers ----------

    public async Task<List<CustomerDto>> SearchCustomersAsync(string? q, CancellationToken ct = default)
    {
        var query = db.Customers.AsNoTracking().Where(c => c.IsActive); // merged duplicates are hidden
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(c => c.Phone!.StartsWith(q.Trim()) || c.Name.Contains(q.Trim()));
        return await query.OrderBy(c => c.Name).Take(50).Select(c => new CustomerDto(c.Id, c.Name, c.Phone, c.CreditLimit)).ToListAsync(ct);
    }

    public async Task<CustomerDto> CreateCustomerAsync(CustomerInput input, CancellationToken ct = default)
    {
        var phone = NormalizePhone(input.Phone);
        Require(!string.IsNullOrWhiteSpace(input.Name), "Customer name is required.");
        Require(input.CreditLimit is null or >= 0, "Credit limit cannot be negative.");
        if (phone is not null && await db.Customers.AnyAsync(c => c.Phone == phone, ct))
            throw BusinessException.Conflict("duplicate_customer", "A customer with this mobile number already exists.");
        var c = new Customer { Name = input.Name.Trim(), Phone = phone, CreditLimit = input.CreditLimit, CreatedAt = clock.UtcNow };
        db.Customers.Add(c);
        audit.Record("customer.created", nameof(Customer), c.Id, c.Name);
        await db.SaveChangesAsync(ct);
        return new CustomerDto(c.Id, c.Name, c.Phone, c.CreditLimit);
    }

    public async Task<CustomerDto> UpdateCustomerAsync(Guid id, CustomerInput input, CancellationToken ct = default)
    {
        var c = await db.Customers.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw BusinessException.NotFound("Customer");
        var phone = NormalizePhone(input.Phone);
        Require(!string.IsNullOrWhiteSpace(input.Name), "Customer name is required.");
        Require(input.CreditLimit is null or >= 0, "Credit limit cannot be negative.");
        if (phone is not null && await db.Customers.AnyAsync(x => x.Id != id && x.Phone == phone, ct))
            throw BusinessException.Conflict("duplicate_customer", "Another customer already has this mobile number.");
        var limitChanged = c.CreditLimit != input.CreditLimit;
        c.Name = input.Name.Trim();
        c.Phone = phone;
        c.CreditLimit = input.CreditLimit;
        audit.Record(limitChanged ? "customer.credit_limit_changed" : "customer.updated", nameof(Customer), c.Id,
            limitChanged ? $"{c.Name} limit → {input.CreditLimit?.ToString("0.00") ?? "none"}" : c.Name);
        await db.SaveChangesAsync(ct);
        return new CustomerDto(c.Id, c.Name, c.Phone, c.CreditLimit);
    }

    /// <summary>Find by phone or create. Used by POS so the cashier never creates duplicates for the same number.</summary>
    public async Task<Customer> FindOrCreateCustomerAsync(CustomerInput input, CancellationToken ct = default)
    {
        var phone = NormalizePhone(input.Phone);
        if (phone is not null)
        {
            var existing = await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone, ct);
            if (existing is not null) return existing;
        }
        Require(!string.IsNullOrWhiteSpace(input.Name), "Customer name is required.");
        var c = new Customer { Name = input.Name.Trim(), Phone = phone, CreatedAt = clock.UtcNow };
        db.Customers.Add(c);
        return c;
    }

    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return null;
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        Require(digits.Length == 10, "Phone must be a 10-digit mobile number.");
        return digits;
    }

    // ---------- helpers ----------

    private async Task SaveWithConcurrencyAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw BusinessException.Conflict("concurrency", "This record was changed by someone else. Reload and try again.");
        }
    }

    internal static (int, int) NormalizePaging(int page, int pageSize) => (Math.Max(1, page), Math.Clamp(pageSize, 1, 200));
    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
