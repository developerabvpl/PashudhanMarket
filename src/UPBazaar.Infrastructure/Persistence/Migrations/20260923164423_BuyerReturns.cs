using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuyerReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shipments_OrderPartId",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_PaymentId_OrderPartId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                schema: "shipping",
                table: "Shipments",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "Forward");

            migrationBuilder.AlterColumn<long>(
                name: "PaymentId",
                schema: "payments",
                table: "Refunds",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "payments",
                table: "Refunds",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "INR");

            migrationBuilder.AddColumn<string>(
                name: "Method",
                schema: "payments",
                table: "Refunds",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Razorpay");

            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                schema: "payments",
                table: "Refunds",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "OrderNumber",
                schema: "payments",
                table: "Refunds",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "UpiId",
                schema: "payments",
                table: "Refunds",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            // Every refund so far came out of a payment; copy its order onto it, now that a UPI
            // refund of cash paid at the door can exist without one.
            migrationBuilder.Sql(
                """
                UPDATE r SET r.OrderId = p.OrderId, r.OrderNumber = p.OrderNumber, r.Currency = p.Currency
                FROM payments.Refunds r
                JOIN payments.Payments p ON p.Id = r.PaymentId
                """);

            // Parts delivered before this have no delivery time, so no return window: they cannot
            // be returned through the buyer flow. Support handles any that need it.
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveredAtUtc",
                schema: "orders",
                table: "OrderParts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnComment",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnDecidedAtUtc",
                schema: "orders",
                table: "OrderParts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnDecidedBy",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnDecisionNote",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnRefundUpiId",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnRequestStatus",
                schema: "orders",
                table: "OrderParts",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnRequestedAtUtc",
                schema: "orders",
                table: "OrderParts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnWindowClosesAtUtc",
                schema: "orders",
                table: "OrderParts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderPartId_Direction",
                schema: "shipping",
                table: "Shipments",
                columns: new[] { "OrderPartId", "Direction" },
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OrderId",
                schema: "payments",
                table: "Refunds",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_OrderPartId",
                schema: "payments",
                table: "Refunds",
                column: "OrderPartId",
                unique: true,
                filter: "[OrderPartId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentId",
                schema: "payments",
                table: "Refunds",
                column: "PaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderParts_ReturnRequestStatus_ReturnRequestedAtUtc",
                schema: "orders",
                table: "OrderParts",
                columns: new[] { "ReturnRequestStatus", "ReturnRequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Shipments_OrderPartId_Direction",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_OrderId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_OrderPartId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_Refunds_PaymentId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropIndex(
                name: "IX_OrderParts_ReturnRequestStatus_ReturnRequestedAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "Direction",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "Method",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "OrderNumber",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "UpiId",
                schema: "payments",
                table: "Refunds");

            migrationBuilder.DropColumn(
                name: "DeliveredAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnComment",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnDecidedAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnDecidedBy",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnDecisionNote",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnRefundUpiId",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnRequestStatus",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnRequestedAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.DropColumn(
                name: "ReturnWindowClosesAtUtc",
                schema: "orders",
                table: "OrderParts");

            migrationBuilder.AlterColumn<long>(
                name: "PaymentId",
                schema: "payments",
                table: "Refunds",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderPartId",
                schema: "shipping",
                table: "Shipments",
                column: "OrderPartId",
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentId_OrderPartId",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "PaymentId", "OrderPartId" },
                unique: true,
                filter: "[OrderPartId] IS NOT NULL");
        }
    }
}
