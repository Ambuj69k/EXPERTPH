using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaOS.Core.Common;
using PharmaOS.Core.Data;
using PharmaOS.Core.Services;

namespace PharmaOS.Core;

public static class ServiceRegistration
{
    public static IServiceCollection AddPharmaCore(this IServiceCollection services, string connectionString, string? fileRoot = null)
    {
        // uploaded files (prescriptions) — keep on a data drive; default is next to the app, never the system drive temp
        services.AddSingleton<IFileStore>(new LocalFileStore(fileRoot ?? Path.Combine(AppContext.BaseDirectory, "files")));
        services.AddScoped<PublicStoreService>();
        services.AddScoped<OnlineOrderService>();
        services.AddDbContext<PharmaDbContext>(o => o.UseSqlServer(connectionString, sql => sql.EnableRetryOnFailure(0)));
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<SequenceService>();
        services.AddScoped<AuditService>();
        services.AddScoped<InventoryService>();
        services.AddScoped<AccountService>();
        services.AddScoped<MasterDataService>();
        services.AddScoped<PurchaseService>();
        services.AddScoped<SalesService>();
        services.AddScoped<StockService>();
        services.AddScoped<PriceService>();
        services.AddScoped<DistributorService>();
        services.AddScoped<SfaService>();
        services.AddScoped<HospitalService>();
        services.AddScoped<ManufacturingService>();
        services.AddScoped<ControlledDrugService>();
        services.AddScoped<PurchaseReturnService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<ShiftService>();
        services.AddScoped<StockCountService>();
        services.AddScoped<RecallService>();
        services.AddScoped<LoyaltyService>();
        services.AddScoped<CrmService>();
        services.AddScoped<PurchasingService>();
        services.AddScoped<ImportService>();
        services.AddScoped<AccountingService>();
        services.AddScoped<MultiStoreService>();
        services.AddScoped<SubscriptionService>();
        services.AddScoped<TenantSupportService>();
        services.AddScoped<ExportService>();
        services.AddScoped<PlatformService>();
        services.AddSingleton<IInvoiceExtractor, NotConfiguredInvoiceExtractor>();
        return services;
    }
}
