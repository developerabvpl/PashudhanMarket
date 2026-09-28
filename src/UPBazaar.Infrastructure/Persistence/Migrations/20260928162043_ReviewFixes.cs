using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "catalog",
                table: "Products",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings",
                sql: "[NetAmount] >= 0 OR [Kind] IN ('CourierCost', 'Adjustment')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "catalog",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings",
                sql: "[NetAmount] >= 0 OR [Kind] = 'CourierCost'");
        }
    }
}
