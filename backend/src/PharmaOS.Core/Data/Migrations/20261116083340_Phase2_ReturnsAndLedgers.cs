using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2_ReturnsAndLedgers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CreditLimit",
                table: "Customers",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CustomerReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ReceiptDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceipts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClaimAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    AcceptedAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WrittenOffAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SupplierCreditNoteNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreditNoteDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SettledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPayments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PaymentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPayments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_CustomerReceipts_CustomerReceiptId",
                        column: x => x.CustomerReceiptId,
                        principalTable: "CustomerReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerReceiptAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    ClaimAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_PurchaseReturns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierPaymentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierPaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_SupplierPayments_SupplierPaymentId",
                        column: x => x.SupplierPaymentId,
                        principalTable: "SupplierPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierPaymentAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_CustomerReceiptId",
                table: "CustomerReceiptAllocations",
                column: "CustomerReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_SalesInvoiceId",
                table: "CustomerReceiptAllocations",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceiptAllocations_TenantId_SalesInvoiceId",
                table: "CustomerReceiptAllocations",
                columns: new[] { "TenantId", "SalesInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_BranchId",
                table: "CustomerReceipts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_CustomerId",
                table: "CustomerReceipts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TenantId_BranchId_ReceiptNo",
                table: "CustomerReceipts",
                columns: new[] { "TenantId", "BranchId", "ReceiptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomerReceipts_TenantId_CustomerId_ReceiptDate",
                table: "CustomerReceipts",
                columns: new[] { "TenantId", "CustomerId", "ReceiptDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_BatchId",
                table: "PurchaseReturnLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_ProductId",
                table: "PurchaseReturnLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_PurchaseReturnId",
                table: "PurchaseReturnLines",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnLines_TenantId",
                table: "PurchaseReturnLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_BranchId",
                table: "PurchaseReturns",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_SupplierId",
                table: "PurchaseReturns",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_TenantId_BranchId_ReturnNo",
                table: "PurchaseReturns",
                columns: new[] { "TenantId", "BranchId", "ReturnNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_TenantId_SupplierId_Status",
                table: "PurchaseReturns",
                columns: new[] { "TenantId", "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_PurchaseInvoiceId",
                table: "SupplierPaymentAllocations",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_SupplierPaymentId",
                table: "SupplierPaymentAllocations",
                column: "SupplierPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPaymentAllocations_TenantId_PurchaseInvoiceId",
                table: "SupplierPaymentAllocations",
                columns: new[] { "TenantId", "PurchaseInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_BranchId",
                table: "SupplierPayments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_SupplierId",
                table: "SupplierPayments",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_BranchId_PaymentNo",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "BranchId", "PaymentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupplierPayments_TenantId_SupplierId_PaymentDate",
                table: "SupplierPayments",
                columns: new[] { "TenantId", "SupplierId", "PaymentDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerReceiptAllocations");

            migrationBuilder.DropTable(
                name: "PurchaseReturnLines");

            migrationBuilder.DropTable(
                name: "SupplierPaymentAllocations");

            migrationBuilder.DropTable(
                name: "CustomerReceipts");

            migrationBuilder.DropTable(
                name: "PurchaseReturns");

            migrationBuilder.DropTable(
                name: "SupplierPayments");

            migrationBuilder.DropColumn(
                name: "CreditLimit",
                table: "Customers");
        }
    }
}
