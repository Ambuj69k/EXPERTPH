using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PharmaOS.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class Security_MfaLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLogins",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "Users",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                table: "Users",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLogins",
                table: "PlatformUsers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedUntil",
                table: "PlatformUsers",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MfaEnabled",
                table: "PlatformUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MfaSecret",
                table: "PlatformUsers",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailedLogins",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FailedLogins",
                table: "PlatformUsers");

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "PlatformUsers");

            migrationBuilder.DropColumn(
                name: "MfaEnabled",
                table: "PlatformUsers");

            migrationBuilder.DropColumn(
                name: "MfaSecret",
                table: "PlatformUsers");
        }
    }
}
