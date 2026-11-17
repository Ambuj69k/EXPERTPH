using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase7_MultiStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsWarehouse",
                table: "Branches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "StockIndents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndentNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    RequestingBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplyingBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RejectReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockIndents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockIndents_Branches_RequestingBranchId",
                        column: x => x.RequestingBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIndents_Branches_SupplyingBranchId",
                        column: x => x.SupplyingBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIndents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockIndentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockIndentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockIndentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockIndentLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIndentLines_StockIndents_StockIndentId",
                        column: x => x.StockIndentId,
                        principalTable: "StockIndents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIndentLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransfers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransferNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    FromBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToBranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IndentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReceiveNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DispatchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    DispatchedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransfers", x => x.Id);
                    table.CheckConstraint("CK_StockTransfer_DifferentBranches", "[FromBranchId] <> [ToBranchId]");
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_FromBranchId",
                        column: x => x.FromBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Branches_ToBranchId",
                        column: x => x.ToBranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_StockIndents_IndentId",
                        column: x => x.IndentId,
                        principalTable: "StockIndents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransfers_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockTransferLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockTransferId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReceivedQtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockTransferLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_StockTransfers_StockTransferId",
                        column: x => x.StockTransferId,
                        principalTable: "StockTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockTransferLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockIndentLines_ProductId",
                table: "StockIndentLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIndentLines_StockIndentId",
                table: "StockIndentLines",
                column: "StockIndentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIndentLines_TenantId",
                table: "StockIndentLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIndents_RequestingBranchId",
                table: "StockIndents",
                column: "RequestingBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIndents_SupplyingBranchId",
                table: "StockIndents",
                column: "SupplyingBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIndents_TenantId_RequestingBranchId_IndentNo",
                table: "StockIndents",
                columns: new[] { "TenantId", "RequestingBranchId", "IndentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockIndents_TenantId_SupplyingBranchId_Status",
                table: "StockIndents",
                columns: new[] { "TenantId", "SupplyingBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_BatchId",
                table: "StockTransferLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_ProductId",
                table: "StockTransferLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_StockTransferId",
                table: "StockTransferLines",
                column: "StockTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferLines_TenantId",
                table: "StockTransferLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_FromBranchId",
                table: "StockTransfers",
                column: "FromBranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_IndentId",
                table: "StockTransfers",
                column: "IndentId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_FromBranchId_TransferNo",
                table: "StockTransfers",
                columns: new[] { "TenantId", "FromBranchId", "TransferNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_ToBranchId_Status",
                table: "StockTransfers",
                columns: new[] { "TenantId", "ToBranchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_ToBranchId",
                table: "StockTransfers",
                column: "ToBranchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockIndentLines");

            migrationBuilder.DropTable(
                name: "StockTransferLines");

            migrationBuilder.DropTable(
                name: "StockTransfers");

            migrationBuilder.DropTable(
                name: "StockIndents");

            migrationBuilder.DropColumn(
                name: "IsWarehouse",
                table: "Branches");
        }
    }
}
