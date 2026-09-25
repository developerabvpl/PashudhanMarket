using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CourierCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Earnings_OrderPartId_Kind",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.AddColumn<string>(
                name: "QuoteError",
                schema: "shipping",
                table: "Shipments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuotedCourierId",
                schema: "shipping",
                table: "Shipments",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "QuotedFreight",
                schema: "shipping",
                table: "Shipments",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnReason",
                schema: "shipping",
                table: "Shipments",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pincode",
                schema: "shipping",
                table: "PickupLocations",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CourierCostAmount",
                schema: "settlements",
                table: "Payouts",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                schema: "settlements",
                table: "Earnings",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reference",
                schema: "settlements",
                table: "Earnings",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShipmentCharges",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<long>(type: "bigint", nullable: false),
                    Trip = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Billed = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    BillCount = table.Column<int>(type: "int", nullable: false),
                    IncurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CorrectedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentCharges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentCharges_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "shipping",
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Earnings_OrderPartId_Kind_Reference",
                schema: "settlements",
                table: "Earnings",
                columns: new[] { "OrderPartId", "Kind", "Reference" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings",
                sql: "[NetAmount] >= 0 OR [Kind] = 'CourierCost'");

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCharges_PublicId",
                schema: "shipping",
                table: "ShipmentCharges",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentCharges_ShipmentId_Trip",
                schema: "shipping",
                table: "ShipmentCharges",
                columns: new[] { "ShipmentId", "Trip" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShipmentCharges",
                schema: "shipping");

            migrationBuilder.DropIndex(
                name: "IX_Earnings_OrderPartId_Kind_Reference",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropColumn(
                name: "QuoteError",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "QuotedCourierId",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "QuotedFreight",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "ReturnReason",
                schema: "shipping",
                table: "Shipments");

            migrationBuilder.DropColumn(
                name: "Pincode",
                schema: "shipping",
                table: "PickupLocations");

            migrationBuilder.DropColumn(
                name: "CourierCostAmount",
                schema: "settlements",
                table: "Payouts");

            migrationBuilder.DropColumn(
                name: "Detail",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.DropColumn(
                name: "Reference",
                schema: "settlements",
                table: "Earnings");

            migrationBuilder.CreateIndex(
                name: "IX_Earnings_OrderPartId_Kind",
                schema: "settlements",
                table: "Earnings",
                columns: new[] { "OrderPartId", "Kind" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Earnings_NetAmount",
                schema: "settlements",
                table: "Earnings",
                sql: "[NetAmount] >= 0");
        }
    }
}
