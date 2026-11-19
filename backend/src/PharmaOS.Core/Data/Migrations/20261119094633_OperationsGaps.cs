using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class OperationsGaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deviations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviationNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ProductionBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RootCause = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CorrectiveAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PreventiveAction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CapaOwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CapaDueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RaisedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RaisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ClosureSignature = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deviations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deviations_ProductionBatches_ProductionBatchId",
                        column: x => x.ProductionBatchId,
                        principalTable: "ProductionBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Deviations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Dispatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Vehicle = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DepartedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Dispatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Dispatches_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Dispatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseClaims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Km = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ExpenseClaims_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SfaTargets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    CallsTarget = table.Column<int>(type: "int", nullable: false),
                    CoverageTargetPercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SfaTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SfaTargets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SfaTargets_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DispatchStops",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DispatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TradeInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Seq = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchStops", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Dispatches_DispatchId",
                        column: x => x.DispatchId,
                        principalTable: "Dispatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DispatchStops_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DispatchStops_TradeInvoices_TradeInvoiceId",
                        column: x => x.TradeInvoiceId,
                        principalTable: "TradeInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_ProductionBatchId",
                table: "Deviations",
                column: "ProductionBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_TenantId_DeviationNo",
                table: "Deviations",
                columns: new[] { "TenantId", "DeviationNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Deviations_TenantId_ProductionBatchId_Status",
                table: "Deviations",
                columns: new[] { "TenantId", "ProductionBatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_BranchId",
                table: "Dispatches",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Dispatches_TenantId_BranchId_DispatchNo",
                table: "Dispatches",
                columns: new[] { "TenantId", "BranchId", "DispatchNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_DispatchId",
                table: "DispatchStops",
                column: "DispatchId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_TenantId_TradeInvoiceId",
                table: "DispatchStops",
                columns: new[] { "TenantId", "TradeInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchStops_TradeInvoiceId",
                table: "DispatchStops",
                column: "TradeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_RepUserId",
                table: "ExpenseClaims",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseClaims_TenantId_RepUserId_Date",
                table: "ExpenseClaims",
                columns: new[] { "TenantId", "RepUserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_SfaTargets_RepUserId",
                table: "SfaTargets",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SfaTargets_TenantId_RepUserId_Month",
                table: "SfaTargets",
                columns: new[] { "TenantId", "RepUserId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deviations");

            migrationBuilder.DropTable(
                name: "DispatchStops");

            migrationBuilder.DropTable(
                name: "ExpenseClaims");

            migrationBuilder.DropTable(
                name: "SfaTargets");

            migrationBuilder.DropTable(
                name: "Dispatches");
        }
    }
}
