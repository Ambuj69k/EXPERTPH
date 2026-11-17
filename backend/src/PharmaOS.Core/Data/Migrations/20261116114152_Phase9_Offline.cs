using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase9_Offline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CapturedOffline",
                table: "SalesInvoices",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OfflineRef",
                table: "SalesInvoices",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CapturedOffline",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "OfflineRef",
                table: "SalesInvoices");
        }
    }
}
