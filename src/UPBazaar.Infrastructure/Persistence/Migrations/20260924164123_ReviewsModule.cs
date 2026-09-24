using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReviewsModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "reviews");

            migrationBuilder.CreateTable(
                name: "ReviewableLines",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewableLines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reviews",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReviewerName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContentStatus = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    ContentSubmittedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModerationNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ModeratedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModeratedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReplyText = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RepliedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplyHidden = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reviews", x => x.Id);
                    table.CheckConstraint("CK_Reviews_Rating", "[Rating] BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "ReviewPhotos",
                schema: "reviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReviewId = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewPhotos_Reviews_ReviewId",
                        column: x => x.ReviewId,
                        principalSchema: "reviews",
                        principalTable: "Reviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewableLines_BuyerId_ProductId",
                schema: "reviews",
                table: "ReviewableLines",
                columns: new[] { "BuyerId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewableLines_OrderPartId_ProductId",
                schema: "reviews",
                table: "ReviewableLines",
                columns: new[] { "OrderPartId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewableLines_PublicId",
                schema: "reviews",
                table: "ReviewableLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewPhotos_PublicId",
                schema: "reviews",
                table: "ReviewPhotos",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewPhotos_ReviewId",
                schema: "reviews",
                table: "ReviewPhotos",
                column: "ReviewId");

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_BuyerId_ProductId",
                schema: "reviews",
                table: "Reviews",
                columns: new[] { "BuyerId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ContentStatus_ContentSubmittedAtUtc",
                schema: "reviews",
                table: "Reviews",
                columns: new[] { "ContentStatus", "ContentSubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_ProductId_CreatedAtUtc",
                schema: "reviews",
                table: "Reviews",
                columns: new[] { "ProductId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_PublicId",
                schema: "reviews",
                table: "Reviews",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reviews_SellerId_CreatedAtUtc",
                schema: "reviews",
                table: "Reviews",
                columns: new[] { "SellerId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewableLines",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "ReviewPhotos",
                schema: "reviews");

            migrationBuilder.DropTable(
                name: "Reviews",
                schema: "reviews");
        }
    }
}
