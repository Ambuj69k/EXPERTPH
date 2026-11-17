using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase6_Accounting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "BooksLockedUntil",
                table: "Tenants",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "JournalEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EntryNo = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    SourceType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Event = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SourceNo = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Narration = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ReversesEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    SystemKey = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JournalLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JournalEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AccountId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Debit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Credit = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    PartyId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalLines", x => x.Id);
                    table.CheckConstraint("CK_JournalLine_OneSided", "([Debit] >= 0 AND [Credit] >= 0 AND ([Debit] = 0 OR [Credit] = 0))");
                    table.ForeignKey(
                        name: "FK_JournalLines_JournalEntries_JournalEntryId",
                        column: x => x.JournalEntryId,
                        principalTable: "JournalEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_LedgerAccounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "LedgerAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JournalLines_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_BranchId",
                table: "JournalEntries",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_BranchId_EntryNo",
                table: "JournalEntries",
                columns: new[] { "TenantId", "BranchId", "EntryNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_Date",
                table: "JournalEntries",
                columns: new[] { "TenantId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_SourceType_SourceId_Event",
                table: "JournalEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Event" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_AccountId",
                table: "JournalLines",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_JournalEntryId",
                table: "JournalLines",
                column: "JournalEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_TenantId_AccountId",
                table: "JournalLines",
                columns: new[] { "TenantId", "AccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_JournalLines_TenantId_PartyId",
                table: "JournalLines",
                columns: new[] { "TenantId", "PartyId" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TenantId_Code",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_TenantId_SystemKey",
                table: "LedgerAccounts",
                columns: new[] { "TenantId", "SystemKey" },
                unique: true,
                filter: "[SystemKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JournalLines");

            migrationBuilder.DropTable(
                name: "JournalEntries");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropColumn(
                name: "BooksLockedUntil",
                table: "Tenants");
        }
    }
}
