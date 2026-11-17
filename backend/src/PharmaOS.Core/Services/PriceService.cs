using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Domain;
using static PharmaOS.Core.Services.AccountService;

namespace PharmaOS.Core.Services;

public record BranchPriceRow(Guid ProductId, string ProductName, decimal PackSize, string PackUnit, decimal? CurrentMrp, decimal? PricePerPack,
    decimal? DiscountVsMrpPercent, DateTimeOffset? UpdatedAt);
public record SetPriceRequest(decimal? PricePerPack);

/// <summary>Branch-specific selling prices (below MRP). Bills always use min(price, batch MRP).</summary>
public class PriceService(PharmaDbContext db, ITenantContext ctx, IClock clock, AuditService audit)
{
    public async Task<List<BranchPriceRow>> ListAsync(string? q, bool onlyWithPrice, CancellationToken ct = default)
    {
        var today = BusinessCalendar.LocalDate(clock.UtcNow);
        var products = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(q)) products = products.Where(p => p.Name.Contains(q.Trim()));
        var prices = db.BranchProductPrices.AsNoTracking().Where(x => x.BranchId == ctx.BranchId);
        if (onlyWithPrice) products = products.Where(p => prices.Any(x => x.ProductId == p.Id));
        var list = await products.OrderBy(p => p.Name).Take(200)
            .Select(p => new
            {
                p.Id, p.Name, p.PackSize, p.PackUnit,
                Price = prices.Where(x => x.ProductId == p.Id).Select(x => new { x.PricePerPack, x.UpdatedAt }).FirstOrDefault(),
                Mrp = db.StockBalances.Where(s => s.BranchId == ctx.BranchId && s.ProductId == p.Id && s.QtyUnits > 0 && s.Batch!.ExpiryDate >= today)
                    .OrderBy(s => s.Batch!.ExpiryDate).Select(s => (decimal?)s.Batch!.Mrp).FirstOrDefault(),
            }).ToListAsync(ct);
        return list.Select(x => new BranchPriceRow(x.Id, x.Name, x.PackSize, x.PackUnit, x.Mrp, x.Price?.PricePerPack,
            x.Price is null || x.Mrp is null or 0 ? null : Money.R2((1 - x.Price.PricePerPack / x.Mrp.Value) * 100), x.Price?.UpdatedAt)).ToList();
    }

    public async Task SetAsync(Guid productId, decimal? pricePerPack, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct)) throw BusinessException.NotFound("Product");
        var existing = await db.BranchProductPrices.SingleOrDefaultAsync(x => x.BranchId == ctx.BranchId && x.ProductId == productId, ct);
        if (pricePerPack is null)
        {
            if (existing is not null) db.BranchProductPrices.Remove(existing);
            audit.Record("price.removed", nameof(Product), productId, null);
        }
        else
        {
            Require(pricePerPack > 0 && pricePerPack <= 1_000_000, "Price must be positive.");
            existing ??= db.BranchProductPrices.Add(new BranchProductPrice { BranchId = ctx.BranchId, ProductId = productId }).Entity;
            existing.PricePerPack = Money.R2(pricePerPack.Value);
            existing.UpdatedAt = clock.UtcNow;
            existing.UpdatedByUserId = ctx.UserId;
            audit.Record("price.set", nameof(Product), productId, $"{existing.PricePerPack}/pack");
        }
        await db.SaveChangesAsync(ct);
    }
}
