using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryCharges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryCharge",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeGstRate",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeTax",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeTaxable",
                table: "SalesInvoices",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryCharge",
                table: "Branches",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeGstRate",
                table: "Branches",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 18m); // existing branches get the same default as new ones (CA to confirm)

            migrationBuilder.AddColumn<decimal>(
                name: "FreeDeliveryAbove",
                table: "Branches",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryCharge",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeGstRate",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeTax",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeTaxable",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "DeliveryCharge",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeGstRate",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "FreeDeliveryAbove",
                table: "Branches");
        }
    }
}
