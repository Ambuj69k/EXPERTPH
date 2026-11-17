using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase4_Crm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LoyaltyEnabled",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyMinRedeemPoints",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyPointValue",
                table: "Tenants",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyRupeesPerPoint",
                table: "Tenants",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Customers",
                type: "bit",
                nullable: false,
                defaultValue: true); // existing customers stay active

            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                table: "Customers",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ConsentRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    Granted = table.Column<bool>(type: "bit", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RecordedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentRecords_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ConsentRecords_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Points = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesReturnId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoyaltyEntries_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoyaltyEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Purpose = table.Column<int>(type: "int", nullable: false),
                    TemplateKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Recipient = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ProviderRef = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Error = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RelatedEntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    RelatedEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NotificationLogs_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NotificationTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NotificationTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NotificationTemplates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RefillReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntervalDays = table.Column<int>(type: "int", nullable: false),
                    NextDueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LastContactedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefillReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefillReminders_Branches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefillReminders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefillReminders_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RefillReminders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_IsActive_Name",
                table: "Customers",
                columns: new[] { "TenantId", "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_CustomerId",
                table: "ConsentRecords",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentRecords_TenantId_CustomerId_Channel_Purpose_At",
                table: "ConsentRecords",
                columns: new[] { "TenantId", "CustomerId", "Channel", "Purpose", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_CustomerId",
                table: "LoyaltyEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_TenantId_CustomerId",
                table: "LoyaltyEntries",
                columns: new[] { "TenantId", "CustomerId" });

            migrationBuilder.CreateIndex(
                name: "IX_LoyaltyEntries_TenantId_SalesInvoiceId",
                table: "LoyaltyEntries",
                columns: new[] { "TenantId", "SalesInvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_CustomerId",
                table: "NotificationLogs",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_TenantId_CustomerId_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "TenantId", "CustomerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationLogs_TenantId_Status_CreatedAt",
                table: "NotificationLogs",
                columns: new[] { "TenantId", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_NotificationTemplates_TenantId_Key_Channel",
                table: "NotificationTemplates",
                columns: new[] { "TenantId", "Key", "Channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefillReminders_BranchId",
                table: "RefillReminders",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_RefillReminders_CustomerId",
                table: "RefillReminders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_RefillReminders_ProductId",
                table: "RefillReminders",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_RefillReminders_TenantId_BranchId_Status_NextDueDate",
                table: "RefillReminders",
                columns: new[] { "TenantId", "BranchId", "Status", "NextDueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RefillReminders_TenantId_CustomerId_ProductId",
                table: "RefillReminders",
                columns: new[] { "TenantId", "CustomerId", "ProductId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentRecords");

            migrationBuilder.DropTable(
                name: "LoyaltyEntries");

            migrationBuilder.DropTable(
                name: "NotificationLogs");

            migrationBuilder.DropTable(
                name: "NotificationTemplates");

            migrationBuilder.DropTable(
                name: "RefillReminders");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_IsActive_Name",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "LoyaltyEnabled",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LoyaltyMinRedeemPoints",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointValue",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LoyaltyRupeesPerPoint",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                table: "Customers");
        }
    }
}
