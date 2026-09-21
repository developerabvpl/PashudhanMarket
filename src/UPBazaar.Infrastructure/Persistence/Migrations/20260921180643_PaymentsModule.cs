using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaymentsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateTable(
                name: "Payments",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrderOutcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OrderOutcomeReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Gateway = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GatewayOrderId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    GatewayPaymentId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailure = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedWebhookEvents",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EventId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    EventType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedWebhookEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Refunds",
                schema: "payments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaymentId = table.Column<long>(type: "bigint", nullable: false),
                    OrderPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    GatewayRefundId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RefundedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefundedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Refunds", x => x.Id);
                    table.CheckConstraint("CK_Refunds_Amount", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_Refunds_Payments_PaymentId",
                        column: x => x.PaymentId,
                        principalSchema: "payments",
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GatewayOrderId",
                schema: "payments",
                table: "Payments",
                column: "GatewayOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_GatewayPaymentId",
                schema: "payments",
                table: "Payments",
                column: "GatewayPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_OrderId",
                schema: "payments",
                table: "Payments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_PublicId",
                schema: "payments",
                table: "Payments",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_Status_OrderOutcome_PaidAtUtc",
                schema: "payments",
                table: "Payments",
                columns: new[] { "Status", "OrderOutcome", "PaidAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_EventId",
                schema: "payments",
                table: "ProcessedWebhookEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedWebhookEvents_PublicId",
                schema: "payments",
                table: "ProcessedWebhookEvents",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PaymentId_OrderPartId",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "PaymentId", "OrderPartId" },
                unique: true,
                filter: "[OrderPartId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_PublicId",
                schema: "payments",
                table: "Refunds",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Refunds_Status_CreatedAtUtc",
                schema: "payments",
                table: "Refunds",
                columns: new[] { "Status", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProcessedWebhookEvents",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Refunds",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "Payments",
                schema: "payments");
        }
    }
}
