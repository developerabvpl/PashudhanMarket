using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                schema: "reviews",
                table: "Reviews",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Reviews already written take the order of the latest delivery of their product to
            // their buyer - the same one a new review would be given.
            migrationBuilder.Sql(
                """
                UPDATE r SET OrderId = l.OrderId
                FROM reviews.Reviews AS r
                CROSS APPLY (
                    SELECT TOP (1) OrderId
                    FROM reviews.ReviewableLines
                    WHERE BuyerId = r.BuyerId AND ProductId = r.ProductId
                    ORDER BY DeliveredAtUtc DESC
                ) AS l;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "reviews",
                table: "Reviews");
        }
    }
}
