using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Distributor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Retailers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Gstin = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    StateCode = table.Column<string>(type: "nvarchar(2)", maxLength: 2, nullable: false),
                    DrugLicenseNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DrugLicenseValidTo = table.Column<DateOnly>(type: "date", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreditLimit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreditDays = table.Column<int>(type: "int", nullable: false),
                    SalesmanUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Retailers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Retailers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeSchemes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BuyQtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FreeQtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeSchemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeSchemes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeSchemes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TakenByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TradeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Retailers_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "Retailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeRates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RatePerPack = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeRates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeRates_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeRates_Retailers_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "Retailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeRates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReceiptNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CollectedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeReceipts_Retailers_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "Retailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeReceipts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesOrderLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    InvoiceNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    InvoiceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    InterState = table.Column<bool>(type: "bit", nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RoundOff = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SalesOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeInvoices_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoices_Retailers_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "Retailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoices_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeCreditNotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RetailerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreditNoteNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeCreditNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeCreditNotes_Retailers_RetailerId",
                        column: x => x.RetailerId,
                        principalTable: "Retailers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeCreditNotes_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeCreditNotes_TradeInvoices_TradeInvoiceId",
                        column: x => x.TradeInvoiceId,
                        principalTable: "TradeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FreeQtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RatePerPack = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GrossAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    GstRate = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    LineTotal = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReturnedPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    SchemeId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeInvoiceLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoiceLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoiceLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeInvoiceLines_TradeInvoices_TradeInvoiceId",
                        column: x => x.TradeInvoiceId,
                        principalTable: "TradeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeReceiptAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeReceiptId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeReceiptAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeReceiptAllocations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeReceiptAllocations_TradeInvoices_TradeInvoiceId",
                        column: x => x.TradeInvoiceId,
                        principalTable: "TradeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeReceiptAllocations_TradeReceipts_TradeReceiptId",
                        column: x => x.TradeReceiptId,
                        principalTable: "TradeReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TradeCreditNoteLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeCreditNoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeInvoiceLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TaxAmount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeCreditNoteLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TradeCreditNoteLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeCreditNoteLines_TradeCreditNotes_TradeCreditNoteId",
                        column: x => x.TradeCreditNoteId,
                        principalTable: "TradeCreditNotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TradeCreditNoteLines_TradeInvoiceLines_TradeInvoiceLineId",
                        column: x => x.TradeInvoiceLineId,
                        principalTable: "TradeInvoiceLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Retailers_TenantId_Gstin",
                table: "Retailers",
                columns: new[] { "TenantId", "Gstin" },
                unique: true,
                filter: "[Gstin] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Retailers_TenantId_Name",
                table: "Retailers",
                columns: new[] { "TenantId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Retailers_TenantId_SalesmanUserId",
                table: "Retailers",
                columns: new[] { "TenantId", "SalesmanUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_ProductId",
                table: "SalesOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_SalesOrderId",
                table: "SalesOrderLines",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrderLines_TenantId",
                table: "SalesOrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_BranchId",
                table: "SalesOrders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_RetailerId",
                table: "SalesOrders",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_TenantId_BranchId_OrderNo",
                table: "SalesOrders",
                columns: new[] { "TenantId", "BranchId", "OrderNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesOrders_TenantId_Status",
                table: "SalesOrders",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNoteLines_TenantId",
                table: "TradeCreditNoteLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNoteLines_TradeCreditNoteId",
                table: "TradeCreditNoteLines",
                column: "TradeCreditNoteId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNoteLines_TradeInvoiceLineId",
                table: "TradeCreditNoteLines",
                column: "TradeInvoiceLineId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNotes_RetailerId",
                table: "TradeCreditNotes",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNotes_TenantId_BranchId_CreditNoteNo",
                table: "TradeCreditNotes",
                columns: new[] { "TenantId", "BranchId", "CreditNoteNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNotes_TenantId_ClientRequestId",
                table: "TradeCreditNotes",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeCreditNotes_TradeInvoiceId",
                table: "TradeCreditNotes",
                column: "TradeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoiceLines_BatchId",
                table: "TradeInvoiceLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoiceLines_ProductId",
                table: "TradeInvoiceLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoiceLines_TenantId",
                table: "TradeInvoiceLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoiceLines_TradeInvoiceId",
                table: "TradeInvoiceLines",
                column: "TradeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_BranchId",
                table: "TradeInvoices",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_RetailerId",
                table: "TradeInvoices",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_SalesOrderId",
                table: "TradeInvoices",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_TenantId_BranchId_InvoiceNo",
                table: "TradeInvoices",
                columns: new[] { "TenantId", "BranchId", "InvoiceNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_TenantId_ClientRequestId",
                table: "TradeInvoices",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeInvoices_TenantId_RetailerId_Status",
                table: "TradeInvoices",
                columns: new[] { "TenantId", "RetailerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeRates_ProductId",
                table: "TradeRates",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRates_RetailerId",
                table: "TradeRates",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeRates_TenantId_ProductId_RetailerId",
                table: "TradeRates",
                columns: new[] { "TenantId", "ProductId", "RetailerId" },
                unique: true,
                filter: "[RetailerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceiptAllocations_TenantId_TradeInvoiceId",
                table: "TradeReceiptAllocations",
                columns: new[] { "TenantId", "TradeInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceiptAllocations_TradeInvoiceId",
                table: "TradeReceiptAllocations",
                column: "TradeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceiptAllocations_TradeReceiptId",
                table: "TradeReceiptAllocations",
                column: "TradeReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceipts_RetailerId",
                table: "TradeReceipts",
                column: "RetailerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceipts_TenantId_BranchId_ReceiptNo",
                table: "TradeReceipts",
                columns: new[] { "TenantId", "BranchId", "ReceiptNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TradeReceipts_TenantId_RetailerId",
                table: "TradeReceipts",
                columns: new[] { "TenantId", "RetailerId" });

            migrationBuilder.CreateIndex(
                name: "IX_TradeSchemes_ProductId",
                table: "TradeSchemes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeSchemes_TenantId_ProductId_IsActive",
                table: "TradeSchemes",
                columns: new[] { "TenantId", "ProductId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SalesOrderLines");

            migrationBuilder.DropTable(
                name: "TradeCreditNoteLines");

            migrationBuilder.DropTable(
                name: "TradeRates");

            migrationBuilder.DropTable(
                name: "TradeReceiptAllocations");

            migrationBuilder.DropTable(
                name: "TradeSchemes");

            migrationBuilder.DropTable(
                name: "TradeCreditNotes");

            migrationBuilder.DropTable(
                name: "TradeInvoiceLines");

            migrationBuilder.DropTable(
                name: "TradeReceipts");

            migrationBuilder.DropTable(
                name: "TradeInvoices");

            migrationBuilder.DropTable(
                name: "SalesOrders");

            migrationBuilder.DropTable(
                name: "Retailers");
        }
    }
}
