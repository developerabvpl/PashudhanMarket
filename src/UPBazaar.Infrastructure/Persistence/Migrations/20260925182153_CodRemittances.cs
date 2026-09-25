using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPBazaar.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CodRemittances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AwaitingCash",
                schema: "settlements",
                table: "Earnings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "CodReceivables",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShipmentId = table.Column<long>(type: "bigint", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    OrderPartId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Awb = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Expected = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Received = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    DeliveredAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CashInAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WriteOffNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    WrittenOffBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodReceivables", x => x.Id);
                    table.CheckConstraint("CK_CodReceivables_Expected", "[Expected] > 0");
                    table.ForeignKey(
                        name: "FK_CodReceivables_Shipments_ShipmentId",
                        column: x => x.ShipmentId,
                        principalSchema: "shipping",
                        principalTable: "Shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CodRemittances",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    RemittedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodRemittances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CodRemittanceLines",
                schema: "shipping",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CodRemittanceId = table.Column<long>(type: "bigint", nullable: false),
                    Awb = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReceivableId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PublicId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CodRemittanceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CodRemittanceLines_CodRemittances_CodRemittanceId",
                        column: x => x.CodRemittanceId,
                        principalSchema: "shipping",
                        principalTable: "CodRemittances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CodReceivables_Awb",
                schema: "shipping",
                table: "CodReceivables",
                column: "Awb");

            migrationBuilder.CreateIndex(
                name: "IX_CodReceivables_OrderPartId",
                schema: "shipping",
                table: "CodReceivables",
                column: "OrderPartId");

            migrationBuilder.CreateIndex(
                name: "IX_CodReceivables_PublicId",
                schema: "shipping",
                table: "CodReceivables",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodReceivables_ShipmentId",
                schema: "shipping",
                table: "CodReceivables",
                column: "ShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodReceivables_Status_DeliveredAtUtc",
                schema: "shipping",
                table: "CodReceivables",
                columns: new[] { "Status", "DeliveredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CodRemittanceLines_Awb_ReceivableId",
                schema: "shipping",
                table: "CodRemittanceLines",
                columns: new[] { "Awb", "ReceivableId" });

            migrationBuilder.CreateIndex(
                name: "IX_CodRemittanceLines_CodRemittanceId",
                schema: "shipping",
                table: "CodRemittanceLines",
                column: "CodRemittanceId");

            migrationBuilder.CreateIndex(
                name: "IX_CodRemittanceLines_PublicId",
                schema: "shipping",
                table: "CodRemittanceLines",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodRemittances_PublicId",
                schema: "shipping",
                table: "CodRemittances",
                column: "PublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CodRemittances_Reference",
                schema: "shipping",
                table: "CodRemittances",
                column: "Reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CodReceivables",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "CodRemittanceLines",
                schema: "shipping");

            migrationBuilder.DropTable(
                name: "CodRemittances",
                schema: "shipping");

            migrationBuilder.DropColumn(
                name: "AwaitingCash",
                schema: "settlements",
                table: "Earnings");
        }
    }
}
