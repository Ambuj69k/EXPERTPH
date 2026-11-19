using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class HospitalPharmacy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Users",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Departments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FormularyItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsHighAlert = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormularyItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormularyItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FormularyItems_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardIndents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IndentNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    EncounterRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RequestedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    FirstIssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardIndents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardIndents_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIndents_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIndents_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardIndentLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIndentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    IssuedUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardIndentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardIndentLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIndentLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIndentLines_WardIndents_WardIndentId",
                        column: x => x.WardIndentId,
                        principalTable: "WardIndents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardIssues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIndentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssueNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    NeedsSecondCheck = table.Column<bool>(type: "bit", nullable: false),
                    PreparedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PreparedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CheckedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IssuedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReceivedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CostValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CancelReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardIssues_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssues_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssues_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssues_WardIndents_WardIndentId",
                        column: x => x.WardIndentId,
                        principalTable: "WardIndents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardIssueLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    WardIndentLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardIssueLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardIssueLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssueLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssueLines_WardIndentLines_WardIndentLineId",
                        column: x => x.WardIndentLineId,
                        principalTable: "WardIndentLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssueLines_WardIssues_WardIssueId",
                        column: x => x.WardIssueId,
                        principalTable: "WardIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardReturns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIssueId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReturnNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CostValue = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardReturns_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardReturns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardReturns_WardIssues_WardIssueId",
                        column: x => x.WardIssueId,
                        principalTable: "WardIssues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardIssueBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIssueLineId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    ReturnedUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardIssueBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardIssueBatches_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssueBatches_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardIssueBatches_WardIssueLines_WardIssueLineId",
                        column: x => x.WardIssueLineId,
                        principalTable: "WardIssueLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WardReturnLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WardIssueBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    QtyUnits = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CostPerUnit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WardReturnLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WardReturnLines_Batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "Batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardReturnLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardReturnLines_WardIssueBatches_WardIssueBatchId",
                        column: x => x.WardIssueBatchId,
                        principalTable: "WardIssueBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WardReturnLines_WardReturns_WardReturnId",
                        column: x => x.WardReturnId,
                        principalTable: "WardReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_TenantId_Code",
                table: "Departments",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormularyItems_ProductId",
                table: "FormularyItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FormularyItems_TenantId_ProductId",
                table: "FormularyItems",
                columns: new[] { "TenantId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WardIndentLines_ProductId",
                table: "WardIndentLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIndentLines_TenantId",
                table: "WardIndentLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIndentLines_WardIndentId",
                table: "WardIndentLines",
                column: "WardIndentId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIndents_BranchId",
                table: "WardIndents",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIndents_DepartmentId",
                table: "WardIndents",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIndents_TenantId_BranchId_IndentNo",
                table: "WardIndents",
                columns: new[] { "TenantId", "BranchId", "IndentNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WardIndents_TenantId_DepartmentId_Status",
                table: "WardIndents",
                columns: new[] { "TenantId", "DepartmentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueBatches_BatchId",
                table: "WardIssueBatches",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueBatches_TenantId",
                table: "WardIssueBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueBatches_WardIssueLineId",
                table: "WardIssueBatches",
                column: "WardIssueLineId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueLines_ProductId",
                table: "WardIssueLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueLines_TenantId",
                table: "WardIssueLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueLines_WardIndentLineId",
                table: "WardIssueLines",
                column: "WardIndentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssueLines_WardIssueId",
                table: "WardIssueLines",
                column: "WardIssueId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssues_BranchId",
                table: "WardIssues",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssues_DepartmentId",
                table: "WardIssues",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WardIssues_TenantId_BranchId_IssueNo",
                table: "WardIssues",
                columns: new[] { "TenantId", "BranchId", "IssueNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WardIssues_TenantId_Status",
                table: "WardIssues",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WardIssues_WardIndentId",
                table: "WardIssues",
                column: "WardIndentId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturnLines_BatchId",
                table: "WardReturnLines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturnLines_TenantId",
                table: "WardReturnLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturnLines_WardIssueBatchId",
                table: "WardReturnLines",
                column: "WardIssueBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturnLines_WardReturnId",
                table: "WardReturnLines",
                column: "WardReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturns_DepartmentId",
                table: "WardReturns",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WardReturns_TenantId_BranchId_ReturnNo",
                table: "WardReturns",
                columns: new[] { "TenantId", "BranchId", "ReturnNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WardReturns_WardIssueId",
                table: "WardReturns",
                column: "WardIssueId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "FormularyItems");

            migrationBuilder.DropTable(
                name: "WardReturnLines");

            migrationBuilder.DropTable(
                name: "WardIssueBatches");

            migrationBuilder.DropTable(
                name: "WardReturns");

            migrationBuilder.DropTable(
                name: "WardIssueLines");

            migrationBuilder.DropTable(
                name: "WardIndentLines");

            migrationBuilder.DropTable(
                name: "WardIssues");

            migrationBuilder.DropTable(
                name: "WardIndents");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Users");
        }
    }
}
