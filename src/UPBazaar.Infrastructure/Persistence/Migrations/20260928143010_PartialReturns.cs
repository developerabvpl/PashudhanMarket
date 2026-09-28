using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PartialReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CouponMinOrder",
                schema: "orders",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnRefundDue",
                schema: "orders",
                table: "OrderParts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnCondition",
                schema: "orders",
                table: "OrderLines",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReturnRequestedQuantity",
                schema: "orders",
                table: "OrderLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ReturnedDiscount",
                schema: "orders",
                table: "OrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                schema: "orders",
                table: "OrderLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "RevokedDiscount",
                schema: "orders",
                table: "OrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Returns made before partial returns were of whole parcels: every unit of every line
            // was asked for, went back once approved, and was found as the parcel was.
            migrationBuilder.Sql(
                """
                UPDATE l SET l.ReturnRequestedQuantity = l.Quantity
                FROM orders.OrderLines l JOIN orders.OrderParts p ON p.Id = l.OrderPartId
                WHERE p.ReturnRequestStatus IS NOT NULL;

                UPDATE l SET l.ReturnedQuantity = l.Quantity, l.ReturnedDiscount = l.Discount
                FROM orders.OrderLines l JOIN orders.OrderParts p ON p.Id = l.OrderPartId
                WHERE p.ReturnRequestStatus = 'Approved';

                UPDATE l SET l.ReturnCondition = p.ReturnCondition
                FROM orders.OrderLines l JOIN orders.OrderParts p ON p.Id = l.OrderPartId
                WHERE p.ReturnCondition IS NOT NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderLines_Return",
                schema: "orders",
                table: "OrderLines",
                sql: "[ReturnRequestedQuantity] BETWEEN 0 AND [Quantity] AND [ReturnedQuantity] BETWEEN 0 AND [ReturnRequestedQuantity]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderLines_Return",
                schema: "orders",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "CouponMinOrder",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReturnRefundDue",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnCondition",
                schema: "orders",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ReturnRequestedQuantity",
                schema: "orders",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ReturnedDiscount",
                schema: "orders",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                schema: "orders",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "RevokedDiscount",
                schema: "orders",
                table: "OrderLines");
        }
    }
}
