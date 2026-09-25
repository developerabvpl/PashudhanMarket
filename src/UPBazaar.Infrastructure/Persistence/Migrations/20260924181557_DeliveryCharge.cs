using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryCharge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Earnings_OrderPartId",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryFee",
                schema: "orders",
                table: "OrderParts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "settlements",
                table: "Earnings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,

                // Every earning made before this was for a sale: there was no delivery charge.
                defaultValue: "Sale");

            migrationBuilder.CreateIndex(
                name: "IX_Earnings_OrderPartId_Kind",
                schema: "settlements",
                table: "Earnings",
                columns: new[] { "OrderPartId", "Kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Earnings_OrderPartId_Kind",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropColumn(
                name: "DeliveryFee",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.CreateIndex(
                name: "IX_Earnings_OrderPartId",
                schema: "settlements",
                table: "Earnings",
                column: "OrderPartId",
                unique: true);
        }
    }
}
