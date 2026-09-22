using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShippingModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "shipping");

            migrationBuilder.AddColumn<decimal>(
                name: "BreadthCm",
                schema: "catalog",
                table: "Products",
                type: "decimal(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                schema: "catalog",
                table: "Products",
                type: "decimal(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                schema: "catalog",
                table: "Products",
                type: "decimal(6,1)",
                precision: 6,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeightGrams",
                schema: "catalog",
                table: "Products",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PickupLocations",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickupLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shipments",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Carrier = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CarrierReference = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PickupLocation = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    WeightGrams = table.Column<int>(type: "int", nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(6,1)", precision: 6, scale: 1, nullable: false),
                    BreadthCm = table.Column<decimal>(type: "decimal(6,1)", precision: 6, scale: 1, nullable: false),
                    HeightCm = table.Column<decimal>(type: "decimal(6,1)", precision: 6, scale: 1, nullable: false),
                    PaymentMode = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    CodAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CarrierOrderId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CarrierShipmentId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Awb = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    CourierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PickupRequestedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shipments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShipmentEvents",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShipmentEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShipmentEvents_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "shipping",
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Package",
                schema: "catalog",
                table: "Products",
                sql: "([WeightGrams] IS NULL AND [LengthCm] IS NULL AND [BreadthCm] IS NULL AND [HeightCm] IS NULL) OR ([WeightGrams] > 0 AND [LengthCm] > 0 AND [BreadthCm] > 0 AND [HeightCm] > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_PickupLocations_PublicId",
                schema: "shipping",
                table: "PickupLocations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PickupLocations_SellerId",
                schema: "shipping",
                table: "PickupLocations",
                column: "SellerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_PublicId",
                schema: "shipping",
                table: "ShipmentEvents",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShipmentEvents_ShipmentId",
                schema: "shipping",
                table: "ShipmentEvents",
                column: "ShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_Awb",
                schema: "shipping",
                table: "Shipments",
                column: "Awb",
                unique: true,
                filter: "[Awb] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderId",
                schema: "shipping",
                table: "Shipments",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderNumber",
                schema: "shipping",
                table: "Shipments",
                column: "OrderNumber");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_OrderPartId",
                schema: "shipping",
                table: "Shipments",
                column: "OrderPartId",
                unique: true,
                filter: "[Status] <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_Shipments_PublicId",
                schema: "shipping",
                table: "Shipments",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PickupLocations",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "ShipmentEvents",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "Shipments",
                schema: "shipping");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Package",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "BreadthCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "WeightGrams",
                schema: "catalog",
                table: "Products");
        }
    }
}
