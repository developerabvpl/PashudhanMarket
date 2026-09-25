using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProductSales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SaleEndsAtUtc",
                schema: "catalog",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice",
                schema: "catalog",
                table: "Products",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SaleStartsAtUtc",
                schema: "catalog",
                table: "Products",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Sale",
                schema: "catalog",
                table: "Products",
                sql: "([SalePrice] IS NULL AND [SaleStartsAtUtc] IS NULL AND [SaleEndsAtUtc] IS NULL) OR ([SalePrice] > 0 AND [SalePrice] < [Price] AND [SaleEndsAtUtc] > [SaleStartsAtUtc])");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Sale",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SaleEndsAtUtc",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SalePrice",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "SaleStartsAtUtc",
                schema: "catalog",
                table: "Products");
        }
    }
}
