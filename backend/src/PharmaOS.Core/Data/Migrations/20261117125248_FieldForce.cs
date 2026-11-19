using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class FieldForce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DailyReports",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DailyReports_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DailyReports_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Territories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ManagerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Territories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Territories_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Territories_Users_ManagerUserId",
                        column: x => x.ManagerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Territories_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TourPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Month = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReviewComment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourPlans_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourPlans_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Doctors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Speciality = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Qualification = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Clinic = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TerritoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Class = table.Column<int>(type: "int", nullable: false),
                    ConsentRecordedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    AnonymisedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<int>(type: "int", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doctors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doctors_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Doctors_Territories_TerritoryId",
                        column: x => x.TerritoryId,
                        principalTable: "Territories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorCalls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DailyReportId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CallAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ProductsDetailed = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Feedback = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    JointWorkUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Latitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    Longitude = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: true),
                    WasPlanned = table.Column<bool>(type: "bit", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorCalls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorCalls_DailyReports_DailyReportId",
                        column: x => x.DailyReportId,
                        principalTable: "DailyReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorCalls_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorCalls_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SampleMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RepUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Qty = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    DoctorCallId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleMovements_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleMovements_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleMovements_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SampleMovements_Users_RepUserId",
                        column: x => x.RepUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TourPlanEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TourPlanId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    DoctorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourPlanEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TourPlanEntries_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourPlanEntries_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TourPlanEntries_TourPlans_TourPlanId",
                        column: x => x.TourPlanId,
                        principalTable: "TourPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_RepUserId",
                table: "DailyReports",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_TenantId_RepUserId_Date",
                table: "DailyReports",
                columns: new[] { "TenantId", "RepUserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyReports_TenantId_Status",
                table: "DailyReports",
                columns: new[] { "TenantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_DoctorCalls_DailyReportId",
                table: "DoctorCalls",
                column: "DailyReportId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorCalls_DoctorId",
                table: "DoctorCalls",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorCalls_TenantId_ClientRequestId",
                table: "DoctorCalls",
                columns: new[] { "TenantId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DoctorCalls_TenantId_DoctorId",
                table: "DoctorCalls",
                columns: new[] { "TenantId", "DoctorId" });

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_TenantId_TerritoryId_Name",
                table: "Doctors",
                columns: new[] { "TenantId", "TerritoryId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_TerritoryId",
                table: "Doctors",
                column: "TerritoryId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleMovements_DoctorId",
                table: "SampleMovements",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleMovements_ProductId",
                table: "SampleMovements",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleMovements_RepUserId",
                table: "SampleMovements",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SampleMovements_TenantId_DoctorCallId",
                table: "SampleMovements",
                columns: new[] { "TenantId", "DoctorCallId" });

            migrationBuilder.CreateIndex(
                name: "IX_SampleMovements_TenantId_RepUserId_ProductId",
                table: "SampleMovements",
                columns: new[] { "TenantId", "RepUserId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_Territories_ManagerUserId",
                table: "Territories",
                column: "ManagerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Territories_RepUserId",
                table: "Territories",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Territories_TenantId_ManagerUserId",
                table: "Territories",
                columns: new[] { "TenantId", "ManagerUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Territories_TenantId_Name",
                table: "Territories",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Territories_TenantId_RepUserId",
                table: "Territories",
                columns: new[] { "TenantId", "RepUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_TourPlanEntries_DoctorId",
                table: "TourPlanEntries",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_TourPlanEntries_TenantId_TourPlanId_Date_DoctorId",
                table: "TourPlanEntries",
                columns: new[] { "TenantId", "TourPlanId", "Date", "DoctorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TourPlanEntries_TourPlanId",
                table: "TourPlanEntries",
                column: "TourPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TourPlans_RepUserId",
                table: "TourPlans",
                column: "RepUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TourPlans_TenantId_RepUserId_Month",
                table: "TourPlans",
                columns: new[] { "TenantId", "RepUserId", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DoctorCalls");

            migrationBuilder.DropTable(
                name: "SampleMovements");

            migrationBuilder.DropTable(
                name: "TourPlanEntries");

            migrationBuilder.DropTable(
                name: "DailyReports");

            migrationBuilder.DropTable(
                name: "Doctors");

            migrationBuilder.DropTable(
                name: "TourPlans");

            migrationBuilder.DropTable(
                name: "Territories");
        }
    }
}
