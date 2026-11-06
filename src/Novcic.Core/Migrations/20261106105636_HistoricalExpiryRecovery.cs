using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalExpiryRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SettlementSource",
                schema: "exchange",
                table: "Instruments",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SettlementReferences",
                schema: "exchange",
                columns: table => new
                {
                    Expiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Source = table.Column<string>(type: "text", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementReferences", x => x.Expiry);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SettlementReferences",
                schema: "exchange");

            migrationBuilder.DropColumn(
                name: "SettlementSource",
                schema: "exchange",
                table: "Instruments");
        }
    }
}
