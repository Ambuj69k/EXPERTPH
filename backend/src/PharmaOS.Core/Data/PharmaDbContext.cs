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
