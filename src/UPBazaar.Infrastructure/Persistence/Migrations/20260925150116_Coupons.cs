using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Coupons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "promotions");

            migrationBuilder.AddColumn<string>(
                name: "CouponCode",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CouponFundedBy",
                schema: "orders",
                table: "Orders",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                schema: "orders",
                table: "OrderLines",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Coupons",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FundedBy = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DiscountType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Value = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxDiscount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    MinOrderValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalLimit = table.Column<int>(type: "int", nullable: true),
                    PerBuyerLimit = table.Column<int>(type: "int", nullable: false),
                    Uses = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint("CK_Coupons_Uses", "[Uses] >= 0");
                    table.CheckConstraint("CK_Coupons_Value", "[Value] > 0 AND ([DiscountType] <> 'Percent' OR [Value] <= 100)");
                });

            migrationBuilder.CreateTable(
                name: "CouponRedemptions",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Discount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    RedeemedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponRedemptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponRedemptions_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalSchema: "promotions",
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CouponSellers",
                schema: "promotions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CouponId = table.Column<long>(type: "bigint", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    JoinedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CouponSellers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CouponSellers_Coupons_CouponId",
                        column: x => x.CouponId,
                        principalSchema: "promotions",
                        principalTable: "Coupons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_CouponId_BuyerId",
                schema: "promotions",
                table: "CouponRedemptions",
                columns: new[] { "CouponId", "BuyerId" });

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_OrderId",
                schema: "promotions",
                table: "CouponRedemptions",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponRedemptions_PublicId",
                schema: "promotions",
                table: "CouponRedemptions",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_Code",
                schema: "promotions",
                table: "Coupons",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_PublicId",
                schema: "promotions",
                table: "Coupons",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_SellerId",
                schema: "promotions",
                table: "Coupons",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_CouponSellers_CouponId_SellerId",
                schema: "promotions",
                table: "CouponSellers",
                columns: new[] { "CouponId", "SellerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CouponSellers_PublicId",
                schema: "promotions",
                table: "CouponSellers",
                column: "PublicId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CouponRedemptions",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "CouponSellers",
                schema: "promotions");

            migrationBuilder.DropTable(
                name: "Coupons",
                schema: "promotions");

            migrationBuilder.DropColumn(
                name: "CouponCode",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CouponFundedBy",
                schema: "orders",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Discount",
                schema: "orders",
                table: "OrderLines");
        }
    }
}
