using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: EF scaffolded the catalog column drops first, which would have thrown
            // every product's stock figure away. The inventory tables are created, the figures
            // copied across, and only then are the old columns dropped. See the end of Up.

            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.CreateTable(
                name: "Reservations",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockItems",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OnHandQuantity = table.Column<int>(type: "int", nullable: false),
                    ReservedQuantity = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockItems", x => x.Id);
                    table.CheckConstraint("CK_StockItems_Quantities", "[ReservedQuantity] >= 0 AND [OnHandQuantity] >= [ReservedQuantity]");
                });

            migrationBuilder.CreateTable(
                name: "ReservationLines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<long>(type: "bigint", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationLines", x => x.Id);
                    table.CheckConstraint("CK_ReservationLines_Quantity", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_ReservationLines_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalSchema: "inventory",
                        principalTable: "Reservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockMovements",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockItemId = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    OnHandChange = table.Column<int>(type: "int", nullable: false),
                    ReservedChange = table.Column<int>(type: "int", nullable: false),
                    OnHandAfter = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    Reference = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecordedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockMovements_StockItems_StockItemId",
                        column: x => x.StockItemId,
                        principalSchema: "inventory",
                        principalTable: "StockItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLines_PublicId",
                schema: "inventory",
                table: "ReservationLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationLines_ReservationId_ProductId",
                schema: "inventory",
                table: "ReservationLines",
                columns: new[] { "ReservationId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_PublicId",
                schema: "inventory",
                table: "Reservations",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_Status_ExpiresAtUtc",
                schema: "inventory",
                table: "Reservations",
                columns: new[] { "Status", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_ProductId",
                schema: "inventory",
                table: "StockItems",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockItems_PublicId",
                schema: "inventory",
                table: "StockItems",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_PublicId",
                schema: "inventory",
                table: "StockMovements",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockItemId_OccurredAtUtc",
                schema: "inventory",
                table: "StockMovements",
                columns: new[] { "StockItemId", "OccurredAtUtc" });

            // Every product gets a stock row carrying its current figures, and a first ledger line
            // saying where they came from, so the ledger explains the opening balance too.
            migrationBuilder.Sql(
                """
                INSERT INTO [inventory].[StockItems]
                    ([ProductId], [OnHandQuantity], [ReservedQuantity], [CreatedAtUtc], [CreatedBy], [PublicId])
                SELECT [PublicId], [OnHandQuantity], [ReservedQuantity], SYSUTCDATETIME(), N'migration', NEWID()
                FROM [catalog].[Products];

                INSERT INTO [inventory].[StockMovements]
                    ([StockItemId], [Type], [OnHandChange], [ReservedChange], [OnHandAfter], [Reason], [OccurredAtUtc], [PublicId])
                SELECT [Id], N'Counted', [OnHandQuantity], [ReservedQuantity], [OnHandQuantity],
                       N'Carried over from the catalogue when Inventory took over stock', [CreatedAtUtc], NEWID()
                FROM [inventory].[StockItems];
                """);

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Stock",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OnHandQuantity",
                schema: "catalog",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReservedQuantity",
                schema: "catalog",
                table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Mirror of Up: the columns come back and are filled before the inventory tables go.
            // The ledger and any open reservations cannot be represented in the old shape and are
            // lost, but every product keeps its stock figure.
            migrationBuilder.AddColumn<int>(
                name: "OnHandQuantity",
                schema: "catalog",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReservedQuantity",
                schema: "catalog",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE p
                SET p.[OnHandQuantity] = s.[OnHandQuantity], p.[ReservedQuantity] = s.[ReservedQuantity]
                FROM [catalog].[Products] p
                JOIN [inventory].[StockItems] s ON s.[ProductId] = p.[PublicId];
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Stock",
                schema: "catalog",
                table: "Products",
                sql: "[ReservedQuantity] >= 0 AND [OnHandQuantity] >= [ReservedQuantity]");

            migrationBuilder.DropTable(
                name: "ReservationLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockMovements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "Reservations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockItems",
                schema: "inventory");
        }
    }
}
