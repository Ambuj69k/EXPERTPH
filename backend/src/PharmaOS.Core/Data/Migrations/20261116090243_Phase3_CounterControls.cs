using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3_CounterControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequireOpenShift",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "BlockReason",
                table: "Batches",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Batches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CashShifts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShiftNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    OpeningCash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ExpectedCash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CountedCash = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Variance = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CloseNotes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashShifts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashShifts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShifts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashShifts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PostedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PostedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCounts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CashShiftId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CashMovements_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashMovements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StockCountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SystemQtyAtCount = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CountedQty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_CashShiftId",
                table: "CashMovements",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_CashMovements_TenantId",
                table: "CashMovements",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_BranchId",
                table: "CashShifts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_TenantId_BranchId_ShiftNo",
                table: "CashShifts",
                columns: new[] { "TenantId", "BranchId", "ShiftNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_TenantId_BranchId_UserId",
                table: "CashShifts",
                columns: new[] { "TenantId", "BranchId", "UserId" },
                unique: true,
                filter: "[Status] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CashShifts_UserId",
                table: "CashShifts",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_BatchId",
                table: "StockCountLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_ProductId",
                table: "StockCountLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountId",
                table: "StockCountLines",
                column: "StockCountId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_TenantId_StockCountId_BatchId",
                table: "StockCountLines",
                columns: new[] { "TenantId", "StockCountId", "BatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_BranchId",
                table: "StockCounts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_TenantId_BranchId_CountNo",
                table: "StockCounts",
                columns: new[] { "TenantId", "BranchId", "CountNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashMovements");

            migrationBuilder.DropTable(
                name: "StockCountLines");

            migrationBuilder.DropTable(
                name: "CashShifts");

            migrationBuilder.DropTable(
                name: "StockCounts");

            migrationBuilder.DropColumn(
                name: "RequireOpenShift",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "BlockReason",
                table: "Batches");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Batches");
        }
    }
}
