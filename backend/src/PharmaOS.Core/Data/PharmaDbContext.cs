using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PharmaOS.Core.Common;
using PharmaOS.Core.Domain;

namespace PharmaOS.Core.Data;

public class PharmaDbContext(DbContextOptions<PharmaDbContext> options, ITenantContext tenant) : DbContext(options)
{
    /// <summary>Read by the global query filters; EF evaluates it per query.</summary>
    public Guid CurrentTenantId => tenant.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<DocumentSequence> DocumentSequences => Set<DocumentSequence>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<BranchProductPrice> BranchProductPrices => Set<BranchProductPrice>();
    public DbSet<Retailer> Retailers => Set<Retailer>();
    public DbSet<TradeRate> TradeRates => Set<TradeRate>();
    public DbSet<TradeScheme> TradeSchemes => Set<TradeScheme>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<TradeInvoice> TradeInvoices => Set<TradeInvoice>();
    public DbSet<TradeInvoiceLine> TradeInvoiceLines => Set<TradeInvoiceLine>();
    public DbSet<TradeCreditNote> TradeCreditNotes => Set<TradeCreditNote>();
    public DbSet<TradeCreditNoteLine> TradeCreditNoteLines => Set<TradeCreditNoteLine>();
    public DbSet<TradeReceipt> TradeReceipts => Set<TradeReceipt>();
    public DbSet<TradeReceiptAllocation> TradeReceiptAllocations => Set<TradeReceiptAllocation>();
    public DbSet<Territory> Territories => Set<Territory>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<TourPlan> TourPlans => Set<TourPlan>();
    public DbSet<TourPlanEntry> TourPlanEntries => Set<TourPlanEntry>();
    public DbSet<DailyReport> DailyReports => Set<DailyReport>();
    public DbSet<DoctorCall> DoctorCalls => Set<DoctorCall>();
    public DbSet<SampleMovement> SampleMovements => Set<SampleMovement>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<FormularyItem> FormularyItems => Set<FormularyItem>();
    public DbSet<WardIndent> WardIndents => Set<WardIndent>();
    public DbSet<WardIndentLine> WardIndentLines => Set<WardIndentLine>();
    public DbSet<WardIssue> WardIssues => Set<WardIssue>();
    public DbSet<WardIssueLine> WardIssueLines => Set<WardIssueLine>();
    public DbSet<WardIssueBatch> WardIssueBatches => Set<WardIssueBatch>();
    public DbSet<WardReturn> WardReturns => Set<WardReturn>();
    public DbSet<WardReturnLine> WardReturnLines => Set<WardReturnLine>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeLine> RecipeLines => Set<RecipeLine>();
    public DbSet<QcSpec> QcSpecs => Set<QcSpec>();
    public DbSet<QcSpecTest> QcSpecTests => Set<QcSpecTest>();
    public DbSet<ProductionBatch> ProductionBatches => Set<ProductionBatch>();
    public DbSet<MaterialConsumption> MaterialConsumptions => Set<MaterialConsumption>();
    public DbSet<QcSample> QcSamples => Set<QcSample>();
    public DbSet<QcResult> QcResults => Set<QcResult>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();
    public DbSet<DispatchStop> DispatchStops => Set<DispatchStop>();
    public DbSet<ExpenseClaim> ExpenseClaims => Set<ExpenseClaim>();
    public DbSet<SfaTarget> SfaTargets => Set<SfaTarget>();
    public DbSet<Deviation> Deviations => Set<Deviation>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Batch> Batches => Set<Batch>();
    public DbSet<StockBalance> StockBalances => Set<StockBalance>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<StockAdjustmentLine> StockAdjustmentLines => Set<StockAdjustmentLine>();
    public DbSet<PurchaseInvoice> PurchaseInvoices => Set<PurchaseInvoice>();
    public DbSet<PurchaseInvoiceLine> PurchaseInvoiceLines => Set<PurchaseInvoiceLine>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<SalesInvoiceLine> SalesInvoiceLines => Set<SalesInvoiceLine>();
    public DbSet<SalesPayment> SalesPayments => Set<SalesPayment>();
    public DbSet<SalesReturn> SalesReturns => Set<SalesReturn>();
    public DbSet<SalesReturnLine> SalesReturnLines => Set<SalesReturnLine>();
    public DbSet<PurchaseReturn> PurchaseReturns => Set<PurchaseReturn>();
    public DbSet<PurchaseReturnLine> PurchaseReturnLines => Set<PurchaseReturnLine>();
    public DbSet<SupplierPayment> SupplierPayments => Set<SupplierPayment>();
    public DbSet<SupplierPaymentAllocation> SupplierPaymentAllocations => Set<SupplierPaymentAllocation>();
    public DbSet<CustomerReceipt> CustomerReceipts => Set<CustomerReceipt>();
    public DbSet<CustomerReceiptAllocation> CustomerReceiptAllocations => Set<CustomerReceiptAllocation>();
    public DbSet<CashShift> CashShifts => Set<CashShift>();
    public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<StockCount> StockCounts => Set<StockCount>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<ConsentRecord> ConsentRecords => Set<ConsentRecord>();
    public DbSet<RefillReminder> RefillReminders => Set<RefillReminder>();
    public DbSet<NotificationTemplate> NotificationTemplates => Set<NotificationTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<LoyaltyEntry> LoyaltyEntries => Set<LoyaltyEntry>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<SupplierProductMap> SupplierProductMaps => Set<SupplierProductMap>();
    public DbSet<LedgerAccount> LedgerAccounts => Set<LedgerAccount>();
    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockIndent> StockIndents => Set<StockIndent>();
    public DbSet<StockIndentLine> StockIndentLines => Set<StockIndentLine>();
    public DbSet<PlatformUser> PlatformUsers => Set<PlatformUser>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<PlatformInvoice> PlatformInvoices => Set<PlatformInvoice>();
    public DbSet<SupportTicket> SupportTickets => Set<SupportTicket>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<OnlineOrder> OnlineOrders => Set<OnlineOrder>();
    public DbSet<OnlineOrderLine> OnlineOrderLines => Set<OnlineOrderLine>();
    public DbSet<OnlineOrderFile> OnlineOrderFiles => Set<OnlineOrderFile>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        // Quantities and money: exact decimals. Values are rounded in code (see Money).
        b.Properties<decimal>().HavePrecision(18, 4);
        b.Properties<string>().HaveMaxLength(256);
        b.Properties<Enum>().HaveConversion<int>();
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Tenant>().Property(x => x.Name).HasMaxLength(200);

        mb.Entity<Branch>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(4); // keeps invoice numbers within 16 chars
            e.Property(x => x.StateCode).HasMaxLength(2);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });

        mb.Entity<AppUser>(e =>
        {
            e.HasIndex(x => x.Email).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.DefaultBranchId);
        });

        mb.Entity<DocumentSequence>(e =>
        {
            e.Property(x => x.DocType).HasMaxLength(10);
            e.Property(x => x.FinancialYear).HasMaxLength(4);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.DocType, x.FinancialYear }).IsUnique();
        });

        mb.Entity<AuditEvent>(e =>
        {
            e.Property(x => x.Summary).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.At });
            e.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId });
        });

        mb.Entity<Product>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Composition).HasMaxLength(500);
            e.Property(x => x.HsnCode).HasMaxLength(8);
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasFilter("[Barcode] IS NOT NULL");
        });

        mb.Entity<Supplier>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name });
        });

        mb.Entity<Customer>(e =>
        {
            e.Property(x => x.Phone).HasMaxLength(15);
            e.HasIndex(x => new { x.TenantId, x.Phone }).IsUnique().HasFilter("[Phone] IS NOT NULL");
        });

        mb.Entity<Batch>(e =>
        {
            e.Property(x => x.BatchNo).HasMaxLength(50);
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.BatchNo, x.Mrp }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ExpiryDate });
        });

        mb.Entity<StockBalance>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.BatchId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
            e.ToTable(t => t.HasCheckConstraint("CK_StockBalance_NonNegative", "[QtyUnits] >= 0"));
        });

        mb.Entity<StockMovement>(e =>
        {
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.BatchId });
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        });

        mb.Entity<StockAdjustment>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.AdjustmentNo }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<StockAdjustmentLine>(e =>
        {
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
        });

        mb.Entity<PurchaseInvoice>(e =>
        {
            e.Property(x => x.SupplierInvoiceNo).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            // Duplicate-invoice protection; a cancelled invoice may be re-entered correctly.
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.SupplierInvoiceNo }).IsUnique().HasFilter("[Status] <> 3");
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.InvoiceDate });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<PurchaseInvoiceLine>(e =>
        {
            e.Property(x => x.BatchNo).HasMaxLength(50);
            e.Property(x => x.GstRate).HasPrecision(5, 2);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
        });

        mb.Entity<SalesInvoice>(e =>
        {
            e.Property(x => x.InvoiceNo).HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.InvoiceNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.BusinessDate });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        mb.Entity<SalesInvoiceLine>(e => e.Property(x => x.GstRate).HasPrecision(5, 2));
        mb.Entity<SalesPayment>(e => e.Property(x => x.Reference).HasMaxLength(100));

        mb.Entity<SalesReturn>(e =>
        {
            e.Property(x => x.ReturnNo).HasMaxLength(16);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ReturnNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.SalesInvoiceId);
        });
        mb.Entity<SalesReturnLine>(e =>
        {
            e.HasOne<SalesInvoiceLine>().WithMany().HasForeignKey(x => x.SalesInvoiceLineId);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        });

        mb.Entity<PurchaseReturn>(e =>
        {
            e.Property(x => x.ReturnNo).HasMaxLength(16);
            e.Property(x => x.SupplierCreditNoteNo).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.RejectReason).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ReturnNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<PurchaseReturnLine>(e => e.Property(x => x.GstRate).HasPrecision(5, 2));

        mb.Entity<SupplierPayment>(e =>
        {
            e.Property(x => x.PaymentNo).HasMaxLength(16);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.PaymentNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.PaymentDate });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<SupplierPaymentAllocation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.PurchaseInvoiceId });
            e.HasOne<PurchaseInvoice>().WithMany().HasForeignKey(x => x.PurchaseInvoiceId);
        });

        mb.Entity<CustomerReceipt>(e =>
        {
            e.Property(x => x.ReceiptNo).HasMaxLength(16);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ReceiptNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.ReceiptDate });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<CustomerReceiptAllocation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.SalesInvoiceId });
            e.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.SalesInvoiceId);
        });

        mb.Entity<CashShift>(e =>
        {
            e.Property(x => x.ShiftNo).HasMaxLength(16);
            e.Property(x => x.CloseNotes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ShiftNo }).IsUnique();
            // one open drawer per user per branch
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.UserId }).IsUnique().HasFilter("[Status] = 1");
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId);
        });
        mb.Entity<CashMovement>(e => e.Property(x => x.Reason).HasMaxLength(200));

        mb.Entity<StockCount>(e =>
        {
            e.Property(x => x.CountNo).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.CountNo }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<StockCountLine>(e => e.HasIndex(x => new { x.TenantId, x.StockCountId, x.BatchId }).IsUnique());

        mb.Entity<Batch>().Property(x => x.BlockReason).HasMaxLength(300);

        mb.Entity<ConsentRecord>(e =>
        {
            e.Property(x => x.Source).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.Channel, x.Purpose, x.At });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        mb.Entity<RefillReminder>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.Status, x.NextDueDate });
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.ProductId });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<NotificationTemplate>(e =>
        {
            e.Property(x => x.Key).HasMaxLength(50);
            e.Property(x => x.Body).HasMaxLength(1000);
            e.HasIndex(x => new { x.TenantId, x.Key, x.Channel }).IsUnique();
        });
        mb.Entity<NotificationLog>(e =>
        {
            e.Property(x => x.TemplateKey).HasMaxLength(50);
            e.Property(x => x.Recipient).HasMaxLength(100);
            e.Property(x => x.Message).HasMaxLength(2000);
            e.Property(x => x.Provider).HasMaxLength(30);
            e.Property(x => x.ProviderRef).HasMaxLength(100);
            e.Property(x => x.Error).HasMaxLength(500);
            e.Property(x => x.RelatedEntityType).HasMaxLength(50);
            e.HasIndex(x => new { x.TenantId, x.CustomerId, x.CreatedAt });
            e.HasIndex(x => new { x.TenantId, x.Status, x.CreatedAt });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        mb.Entity<LoyaltyEntry>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.CustomerId });
            e.HasIndex(x => new { x.TenantId, x.SalesInvoiceId });
            e.HasOne<Customer>().WithMany().HasForeignKey(x => x.CustomerId);
        });
        mb.Entity<Customer>().HasIndex(x => new { x.TenantId, x.IsActive, x.Name });

        mb.Entity<LedgerAccount>(e =>
        {
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.SystemKey).HasMaxLength(30);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SystemKey }).IsUnique().HasFilter("[SystemKey] IS NOT NULL");
        });
        mb.Entity<JournalEntry>(e =>
        {
            e.Property(x => x.EntryNo).HasMaxLength(16);
            e.Property(x => x.SourceType).HasMaxLength(30);
            e.Property(x => x.Event).HasMaxLength(20);
            e.Property(x => x.SourceNo).HasMaxLength(50);
            e.Property(x => x.Narration).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.EntryNo }).IsUnique();
            // posting engine idempotency: one journal per document event (manual entries use their own id as source)
            e.HasIndex(x => new { x.TenantId, x.SourceType, x.SourceId, x.Event }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Date });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<JournalLine>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.AccountId });
            e.HasIndex(x => new { x.TenantId, x.PartyId });
            e.ToTable(t => t.HasCheckConstraint("CK_JournalLine_OneSided", "([Debit] >= 0 AND [Credit] >= 0 AND ([Debit] = 0 OR [Credit] = 0))"));
        });

        mb.Entity<PlatformUser>(e =>
        {
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Email).IsUnique();
        });
        mb.Entity<Subscription>(e =>
        {
            e.Property(x => x.PlanCode).HasMaxLength(20);
            e.Property(x => x.ExtraModules).HasMaxLength(200);
            e.Property(x => x.DisabledModules).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.TenantId).IsUnique();
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });
        mb.Entity<PlatformInvoice>(e =>
        {
            e.Property(x => x.InvoiceNo).HasMaxLength(20);
            e.Property(x => x.PaymentReference).HasMaxLength(100);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasIndex(x => x.InvoiceNo).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId);
        });
        mb.Entity<SupportTicket>(e =>
        {
            e.Property(x => x.Subject).HasMaxLength(200);
            e.Property(x => x.Priority).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Status });
        });
        mb.Entity<SupportMessage>(e => e.Property(x => x.Body).HasMaxLength(4000));

        mb.Entity<Retailer>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Gstin).HasMaxLength(15);
            e.Property(x => x.StateCode).HasMaxLength(2);
            e.Property(x => x.DrugLicenseNo).HasMaxLength(100);
            e.Property(x => x.Route).HasMaxLength(100);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.CreditLimit).HasPrecision(18, 2);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.Name });
            e.HasIndex(x => new { x.TenantId, x.Gstin }).IsUnique().HasFilter("[Gstin] IS NOT NULL");
            e.HasIndex(x => new { x.TenantId, x.SalesmanUserId });
        });
        mb.Entity<TradeRate>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.RetailerId }).IsUnique();
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
            e.HasOne<Retailer>().WithMany().HasForeignKey(x => x.RetailerId);
        });
        mb.Entity<TradeScheme>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.IsActive });
        });
        mb.Entity<SalesOrder>(e =>
        {
            e.Property(x => x.OrderNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.OrderNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<TradeInvoice>(e =>
        {
            e.Property(x => x.InvoiceNo).HasMaxLength(16);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.InvoiceNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RetailerId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<SalesOrder>().WithMany().HasForeignKey(x => x.SalesOrderId);
        });
        mb.Entity<TradeInvoiceLine>(e => e.Property(x => x.GstRate).HasPrecision(5, 2));
        mb.Entity<TradeCreditNote>(e =>
        {
            e.Property(x => x.CreditNoteNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.CreditNoteNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.HasOne<TradeInvoice>().WithMany().HasForeignKey(x => x.TradeInvoiceId);
            e.HasOne<Retailer>().WithMany().HasForeignKey(x => x.RetailerId);
        });
        mb.Entity<TradeCreditNoteLine>(e => e.HasOne<TradeInvoiceLine>().WithMany().HasForeignKey(x => x.TradeInvoiceLineId));
        mb.Entity<TradeReceipt>(e =>
        {
            e.Property(x => x.ReceiptNo).HasMaxLength(16);
            e.Property(x => x.Reference).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ReceiptNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RetailerId });
        });
        mb.Entity<TradeReceiptAllocation>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.TradeInvoiceId });
            e.HasOne<TradeInvoice>().WithMany().HasForeignKey(x => x.TradeInvoiceId);
        });

        mb.Entity<Territory>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.RepUserId });
            e.HasIndex(x => new { x.TenantId, x.ManagerUserId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ManagerUserId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<Doctor>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Speciality).HasMaxLength(100);
            e.Property(x => x.Qualification).HasMaxLength(100);
            e.Property(x => x.Clinic).HasMaxLength(200);
            e.Property(x => x.Area).HasMaxLength(100);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.TerritoryId, x.Name });
        });
        mb.Entity<TourPlan>(e =>
        {
            e.Property(x => x.ReviewComment).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.RepUserId, x.Month }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<TourPlanEntry>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.TourPlanId, x.Date, x.DoctorId }).IsUnique();
            e.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<DailyReport>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.ReviewComment).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.RepUserId, x.Date }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<DoctorCall>(e =>
        {
            e.Property(x => x.ProductsDetailed).HasMaxLength(500);
            e.Property(x => x.Feedback).HasMaxLength(1000);
            e.Property(x => x.Latitude).HasPrecision(9, 6);
            e.Property(x => x.Longitude).HasPrecision(9, 6);
            e.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.DoctorId });
            e.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<SampleMovement>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.RepUserId, x.ProductId });
            e.HasIndex(x => new { x.TenantId, x.DoctorCallId });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Department>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Code).HasMaxLength(10);
            e.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        });
        mb.Entity<AppUser>().HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        mb.Entity<FormularyItem>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique();
        });
        mb.Entity<WardIndent>(e =>
        {
            e.Property(x => x.IndentNo).HasMaxLength(16);
            e.Property(x => x.EncounterRef).HasMaxLength(50);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.IndentNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.DepartmentId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<WardIssue>(e =>
        {
            e.Property(x => x.IssueNo).HasMaxLength(16);
            e.Property(x => x.ReceivedBy).HasMaxLength(100);
            e.Property(x => x.CancelReason).HasMaxLength(300);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.IssueNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<WardIndent>().WithMany().HasForeignKey(x => x.WardIndentId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<WardIssueLine>(e => e.HasOne<WardIndentLine>().WithMany().HasForeignKey(x => x.WardIndentLineId).OnDelete(DeleteBehavior.NoAction));
        mb.Entity<WardReturn>(e =>
        {
            e.Property(x => x.ReturnNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ReturnNo }).IsUnique();
            e.HasOne<WardIssue>().WithMany().HasForeignKey(x => x.WardIssueId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<WardReturnLine>(e =>
        {
            e.HasOne<WardIssueBatch>().WithMany().HasForeignKey(x => x.WardIssueBatchId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Recipe>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.VersionNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique().HasFilter("[Status] = 2"); // one approved version
        });
        mb.Entity<RecipeLine>(e => e.HasOne(x => x.Material).WithMany().HasForeignKey(x => x.MaterialProductId).OnDelete(DeleteBehavior.NoAction));
        mb.Entity<QcSpec>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.VersionNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ProductId }).IsUnique().HasFilter("[Status] = 2");
        });
        mb.Entity<QcSpecTest>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Unit).HasMaxLength(20);
            e.Property(x => x.Requirement).HasMaxLength(300);
        });
        mb.Entity<ProductionBatch>(e =>
        {
            e.Property(x => x.BatchNo).HasMaxLength(30);
            e.Property(x => x.DecisionSignature).HasMaxLength(200);
            e.Property(x => x.DecisionNote).HasMaxLength(500);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.ProductId, x.BatchNo });
            e.HasIndex(x => new { x.TenantId, x.Status });
            e.HasOne(x => x.Recipe).WithMany().HasForeignKey(x => x.RecipeId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Batch>().WithMany().HasForeignKey(x => x.OutputBatchId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<MaterialConsumption>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BatchId });
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<QcSample>(e =>
        {
            e.Property(x => x.SampleNo).HasMaxLength(16);
            e.Property(x => x.TesterSignature).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.BatchId });
            e.HasOne(x => x.Batch).WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.NoAction);
            e.HasOne(x => x.Spec).WithMany().HasForeignKey(x => x.QcSpecId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<QcResult>(e =>
        {
            e.Property(x => x.Text).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.QcSampleId });
            e.HasOne<QcSpecTest>().WithMany().HasForeignKey(x => x.QcSpecTestId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<Dispatch>(e =>
        {
            e.Property(x => x.DispatchNo).HasMaxLength(16);
            e.Property(x => x.Route).HasMaxLength(100);
            e.Property(x => x.Vehicle).HasMaxLength(30);
            e.Property(x => x.DriverName).HasMaxLength(100);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.DispatchNo }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<DispatchStop>(e =>
        {
            e.Property(x => x.ReceivedBy).HasMaxLength(100);
            e.Property(x => x.Note).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.TradeInvoiceId });
            e.HasOne(x => x.Invoice).WithMany().HasForeignKey(x => x.TradeInvoiceId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<ExpenseClaim>(e =>
        {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Note).HasMaxLength(300);
            e.Property(x => x.ReviewComment).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.RepUserId, x.Date });
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<SfaTarget>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.RepUserId, x.Month }).IsUnique();
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RepUserId).OnDelete(DeleteBehavior.NoAction);
        });
        mb.Entity<Deviation>(e =>
        {
            e.Property(x => x.DeviationNo).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.RootCause).HasMaxLength(2000);
            e.Property(x => x.CorrectiveAction).HasMaxLength(2000);
            e.Property(x => x.PreventiveAction).HasMaxLength(2000);
            e.Property(x => x.ClosureSignature).HasMaxLength(200);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.DeviationNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ProductionBatchId, x.Status });
            e.HasOne<ProductionBatch>().WithMany().HasForeignKey(x => x.ProductionBatchId).OnDelete(DeleteBehavior.NoAction);
        });

        mb.Entity<BranchProductPrice>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId }).IsUnique();
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });

        mb.Entity<OnlineOrder>(e =>
        {
            e.Property(x => x.OrderNo).HasMaxLength(16);
            e.Property(x => x.TrackingToken).HasMaxLength(64);
            e.Property(x => x.HandoverCode).HasMaxLength(6);
            e.Property(x => x.CustomerName).HasMaxLength(100);
            e.Property(x => x.CustomerPhone).HasMaxLength(15);
            e.Property(x => x.Address).HasMaxLength(500);
            e.Property(x => x.Pincode).HasMaxLength(6);
            e.Property(x => x.RequestText).HasMaxLength(1000);
            e.Property(x => x.CustomerNotes).HasMaxLength(500);
            e.Property(x => x.PharmacistNotes).HasMaxLength(1000);
            e.Property(x => x.RejectReason).HasMaxLength(300);
            e.Property(x => x.DeliveryFailureReason).HasMaxLength(300);
            e.HasIndex(x => x.TrackingToken).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.OrderNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.Status });
            e.HasIndex(x => new { x.TenantId, x.CustomerPhone, x.CreatedAt });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
            e.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => x.SalesInvoiceId);
        });
        mb.Entity<OnlineOrderFile>(e =>
        {
            e.Property(x => x.FileName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(50);
            e.Property(x => x.StoragePath).HasMaxLength(300);
        });
        mb.Entity<Branch>().Property(x => x.DeliveryPincodes).HasMaxLength(1000);
        mb.Entity<Branch>().Property(x => x.OnlineNotice).HasMaxLength(300);

        mb.Entity<StockTransfer>(e =>
        {
            e.Property(x => x.TransferNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.ReceiveNotes).HasMaxLength(500);
            e.HasIndex(x => new { x.TenantId, x.FromBranchId, x.TransferNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.ToBranchId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.FromBranchId);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.ToBranchId);
            e.HasOne<StockIndent>().WithMany().HasForeignKey(x => x.IndentId);
            e.ToTable(t => t.HasCheckConstraint("CK_StockTransfer_DifferentBranches", "[FromBranchId] <> [ToBranchId]"));
        });
        mb.Entity<StockIndent>(e =>
        {
            e.Property(x => x.IndentNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectReason).HasMaxLength(300);
            e.HasIndex(x => new { x.TenantId, x.RequestingBranchId, x.IndentNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SupplyingBranchId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.RequestingBranchId);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.SupplyingBranchId);
        });

        mb.Entity<PurchaseOrder>(e =>
        {
            e.Property(x => x.PoNo).HasMaxLength(16);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.BranchId, x.PoNo }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.Status });
            e.HasOne<Branch>().WithMany().HasForeignKey(x => x.BranchId);
        });
        mb.Entity<PurchaseInvoice>().HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId);
        mb.Entity<Supplier>().Property(x => x.ImportMappingJson).HasMaxLength(2000);
        mb.Entity<SupplierProductMap>(e =>
        {
            e.Property(x => x.SupplierItemCode).HasMaxLength(50);
            e.Property(x => x.SupplierItemName).HasMaxLength(200);
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.SupplierItemName }).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.SupplierId, x.SupplierItemCode });
            e.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId);
            e.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId);
        });

        foreach (var et in mb.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)).ToList())
        {
            // Every tenant row references a real tenant, and is invisible to other tenants.
            mb.Entity(et.ClrType).HasOne(typeof(Tenant)).WithMany().HasForeignKey(nameof(ITenantOwned.TenantId));
            mb.Entity(et.ClrType).HasQueryFilter(BuildTenantFilter(et.ClrType));
        }

        // Ids are assigned in code (Guid v7). Telling EF they are never store-generated means new child rows
        // added to an already-tracked document are treated as inserts, not updates.
        foreach (var et in mb.Model.GetEntityTypes())
            mb.Entity(et.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();

        // Business history is never cascade-deleted.
        foreach (var fk in mb.Model.GetEntityTypes().SelectMany(t => t.GetForeignKeys()))
            fk.DeleteBehavior = DeleteBehavior.Restrict;
    }

    private LambdaExpression BuildTenantFilter(Type type)
    {
        var p = Expression.Parameter(type, "e");
        var body = Expression.Equal(
            Expression.Property(p, nameof(ITenantOwned.TenantId)),
            Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
        return Expression.Lambda(body, p);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyGuards();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken ct = default)
    {
        ApplyGuards();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, ct);
    }

    private void ApplyGuards()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantOwned owned)
            {
                if (entry.State == EntityState.Added && owned.TenantId == Guid.Empty)
                    owned.TenantId = tenant.TenantId;

                if (entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                    && tenant.TenantId != Guid.Empty && owned.TenantId != tenant.TenantId)
                    throw new InvalidOperationException("Cross-tenant write blocked.");

                if (owned.TenantId == Guid.Empty && entry.State == EntityState.Added)
                    throw new InvalidOperationException($"{entry.Entity.GetType().Name} has no tenant.");
            }

            if (entry.Entity is IImmutable && entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is immutable.");

            if (entry.Entity is IVersioned v && entry.State == EntityState.Modified)
                v.Version++;
        }
    }
}
