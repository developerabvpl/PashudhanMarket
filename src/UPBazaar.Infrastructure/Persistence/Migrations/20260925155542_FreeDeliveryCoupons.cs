using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FreeDeliveryCoupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_Value",
                schema: "promotions",
                table: "Coupons");

            migrationBuilder.AddColumn<bool>(
                name: "FreeDelivery",
                schema: "orders",
                table: "OrderParts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_Value",
                schema: "promotions",
                table: "Coupons",
                sql: "([DiscountType] = 'FreeDelivery' AND [Value] = 0) OR ([DiscountType] <> 'FreeDelivery' AND [Value] > 0 AND ([DiscountType] <> 'Percent' OR [Value] <= 100))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_Value",
                schema: "promotions",
                table: "Coupons");

            migrationBuilder.DropColumn(
                name: "FreeDelivery",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_Value",
                schema: "promotions",
                table: "Coupons",
                sql: "[Value] > 0 AND ([DiscountType] <> 'Percent' OR [Value] <= 100)");
        }
    }
}
