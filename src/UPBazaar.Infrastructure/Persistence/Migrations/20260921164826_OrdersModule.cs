using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrdersModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PaymentMethod = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    PaymentStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    ShippingFee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlacedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PaymentDueAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PaymentReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    DeliveryCity = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeliveryDistrict = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DeliveryFullName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DeliveryLandmark = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeliveryLine1 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DeliveryLine2 = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DeliveryMobile = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    DeliveryPincode = table.Column<string>(type: "nvarchar(6)", maxLength: 6, nullable: false),
                    DeliveryState = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrderParts",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CancellationReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderParts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderParts_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "orders",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderLines",
                schema: "orders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderPartId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Sku = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.CheckConstraint("CK_OrderLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_OrderLines_OrderParts_OrderPartId",
                        column: x => x.OrderPartId,
                        principalSchema: "orders",
                        principalTable: "OrderParts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderPartId",
                schema: "orders",
                table: "OrderLines",
                column: "OrderPartId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_ProductId",
                schema: "orders",
                table: "OrderLines",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_PublicId",
                schema: "orders",
                table: "OrderLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderParts_OrderId",
                schema: "orders",
                table: "OrderParts",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderParts_PublicId",
                schema: "orders",
                table: "OrderParts",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderParts_SellerId_Status",
                schema: "orders",
                table: "OrderParts",
                columns: new[] { "SellerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BuyerId_PlacedAtUtc",
                schema: "orders",
                table: "Orders",
                columns: new[] { "BuyerId", "PlacedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Number",
                schema: "orders",
                table: "Orders",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PublicId",
                schema: "orders",
                table: "Orders",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status_PaymentDueAtUtc",
                schema: "orders",
                table: "Orders",
                columns: new[] { "Status", "PaymentDueAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrderLines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "OrderParts",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "orders");
        }
    }
}
