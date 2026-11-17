using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase10_Online : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "OnlineListed",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryPincodes",
                table: "Branches",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OnlineNotice",
                table: "Branches",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlineOrdersEnabled",
                table: "Branches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OnlineOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    TrackingToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    HandoverCode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerPhone = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Pincode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: true),
                    Fulfilment = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CustomerNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PharmacistNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PrescriberName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeliveryAgentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DeliveryAttempts = table.Column<int>(type: "int", nullable: false),
                    DeliveryFailureReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlineOrderFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnlineOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineOrderFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineOrderFiles_OnlineOrders_OnlineOrderId",
                        column: x => x.OnlineOrderId,
                        principalTable: "OnlineOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrderFiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OnlineOrderLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnlineOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestedQtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ApprovedQtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    AddedByPharmacist = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnlineOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OnlineOrderLines_OnlineOrders_OnlineOrderId",
                        column: x => x.OnlineOrderId,
                        principalTable: "OnlineOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OnlineOrderLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderFiles_OnlineOrderId",
                table: "OnlineOrderFiles",
                column: "OnlineOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderFiles_TenantId",
                table: "OnlineOrderFiles",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderLines_OnlineOrderId",
                table: "OnlineOrderLines",
                column: "OnlineOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderLines_ProductId",
                table: "OnlineOrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrderLines_TenantId",
                table: "OnlineOrderLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_BranchId",
                table: "OnlineOrders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_SalesInvoiceId",
                table: "OnlineOrders",
                column: "SalesInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_TenantId_BranchId_OrderNo",
                table: "OnlineOrders",
                columns: new[] { "TenantId", "BranchId", "OrderNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_TenantId_BranchId_Status",
                table: "OnlineOrders",
                columns: new[] { "TenantId", "BranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_TenantId_CustomerPhone_CreatedAt",
                table: "OnlineOrders",
                columns: new[] { "TenantId", "CustomerPhone", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OnlineOrders_TrackingToken",
                table: "OnlineOrders",
                column: "TrackingToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnlineOrderFiles");

            migrationBuilder.DropTable(
                name: "OnlineOrderLines");

            migrationBuilder.DropTable(
                name: "OnlineOrders");

            migrationBuilder.DropColumn(
                name: "OnlineListed",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "DeliveryPincodes",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OnlineNotice",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "OnlineOrdersEnabled",
                table: "Branches");
        }
    }
}
