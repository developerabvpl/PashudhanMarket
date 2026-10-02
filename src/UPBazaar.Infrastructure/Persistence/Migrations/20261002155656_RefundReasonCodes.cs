using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RefundReasonCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReasonCode",
                schema: "payments",
                table: "Refunds",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            // Refunds recorded before the code existed take it from the sentence they were
            // written with - the exact sentences the Payments module has ever written, copied here
            // rather than referenced so a later rewording cannot change what this migration did.
            // Until the whole-order sentence existed, a whole-order cancellation was also written
            // as "Part of the order was cancelled.", and is left as PartCancelled: the code then
            // says what the text says. Anything else stays null and the portal shows its text.
            migrationBuilder.Sql(BackfillSql);
        }

        /// <summary>The backfill, public so a test can run it over refunds written the old way.</summary>
        public const string BackfillSql =
            """
            UPDATE payments.Refunds SET ReasonCode =
                CASE
                    WHEN Reason = N'The order was cancelled.' THEN N'OrderCancelled'
                    WHEN Reason = N'Part of the order was cancelled.' THEN N'PartCancelled'
                    WHEN Reason = N'The parcel could not be delivered and went back to the seller.' THEN N'Undelivered'
                    WHEN Reason = N'The buyer returned the parcel and it is back with the seller.' THEN N'BuyerReturn'
                    WHEN Reason LIKE N'Payment could not be applied to the order: %' THEN N'PaymentRefused'
                END
            WHERE ReasonCode IS NULL;
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReasonCode",
                schema: "payments",
                table: "Refunds");
        }
    }
}
