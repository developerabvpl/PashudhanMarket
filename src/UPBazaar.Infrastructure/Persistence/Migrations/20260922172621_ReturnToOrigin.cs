using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReturnToOrigin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnCondition",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnInspectedAtUtc",
                schema: "orders",
                table: "OrderParts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnInspectedBy",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnNote",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnCondition",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnInspectedAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnInspectedBy",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnNote",
                schema: "orders",
                table: "OrderParts");
        }
    }
}
