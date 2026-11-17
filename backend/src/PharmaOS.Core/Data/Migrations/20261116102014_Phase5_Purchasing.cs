using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase5_Purchasing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImportMappingJson",
                table: "Suppliers",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PurchaseOrderId",
                table: "PurchaseInvoices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrderDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SupplierProductMaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SupplierItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierProductMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierProductMaps_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierProductMaps_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SupplierProductMaps_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LineNo = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedQtyPacks = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ExpectedRate = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_PurchaseOrderId",
                table: "PurchaseInvoices",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_ProductId",
                table: "PurchaseOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_TenantId",
                table: "PurchaseOrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_BranchId",
                table: "PurchaseOrders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_SupplierId",
                table: "PurchaseOrders",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_TenantId_BranchId_PoNo",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "BranchId", "PoNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_TenantId_SupplierId_Status",
                table: "PurchaseOrders",
                columns: new[] { "TenantId", "SupplierId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductMaps_ProductId",
                table: "SupplierProductMaps",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductMaps_SupplierId",
                table: "SupplierProductMaps",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductMaps_TenantId_SupplierId_SupplierItemCode",
                table: "SupplierProductMaps",
                columns: new[] { "TenantId", "SupplierId", "SupplierItemCode" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierProductMaps_TenantId_SupplierId_SupplierItemName",
                table: "SupplierProductMaps",
                columns: new[] { "TenantId", "SupplierId", "SupplierItemName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_PurchaseOrders_PurchaseOrderId",
                table: "PurchaseInvoices",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_PurchaseOrders_PurchaseOrderId",
                table: "PurchaseInvoices");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "SupplierProductMaps");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_PurchaseOrderId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "ImportMappingJson",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "PurchaseInvoices");
        }
    }
}
