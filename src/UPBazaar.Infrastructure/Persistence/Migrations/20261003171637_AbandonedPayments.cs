using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AbandonedPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No column changes: the status is stored by name, and Abandoned is a new name. From
            // here on a payment is abandoned when its order is cancelled unpaid; the payments
            // already left behind by orders cancelled before this are caught up here, so they stop
            // reading as awaiting a buyer who can no longer pay.
            migrationBuilder.Sql(BackfillSql);
        }

        /// <summary>The backfill, public so a test can run it over payments left the old way.</summary>
        public const string BackfillSql =
            """
            UPDATE p SET Status = N'Abandoned'
            FROM payments.Payments p
            JOIN orders.Orders o ON o.PublicId = p.OrderId
            WHERE p.Status = N'Created' AND o.Status = N'Cancelled';
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The older code knows no Abandoned and could not read such a row.
            migrationBuilder.Sql("UPDATE payments.Payments SET Status = N'Created' WHERE Status = N'Abandoned';");
        }
    }
}
