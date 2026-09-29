using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderAmountPaid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AmountPaid",
                schema: "orders",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            // Orders already paid online take the amount from the payment that confirmed them:
            // the one whose gateway id the order recorded. And a payment whose order has been
            // cancelled since says so, as it would had it been cancelled after this change.
            migrationBuilder.Sql(
                """
                UPDATE o SET o.AmountPaid = p.Amount
                FROM orders.Orders o
                JOIN payments.Payments p ON p.OrderId = o.PublicId AND p.GatewayPaymentId = o.PaymentReference
                WHERE o.PaymentStatus = 'Paid' AND p.Status = 'Paid';

                UPDATE p SET p.OrderOutcome = 'Cancelled'
                FROM payments.Payments p
                JOIN orders.Orders o ON o.PublicId = p.OrderId
                WHERE p.Status = 'Paid' AND p.OrderOutcome = 'Confirmed' AND o.Status = 'Cancelled';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AmountPaid",
                schema: "orders",
                table: "Orders");
        }
    }
}
