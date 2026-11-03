using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class PnlHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PnlLifecycles",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    Remaining = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    EntryNotional = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ExitNotional = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    OpeningFee = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ClosingFee = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Funding = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlLifecycles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PnlProjection",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LastSequence = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PnlProjection", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PnlLifecycles_UserId_ClosedAt_Id",
                schema: "exchange",
                table: "PnlLifecycles",
                columns: new[] { "UserId", "ClosedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_PnlLifecycles_UserId_InstrumentId",
                schema: "exchange",
                table: "PnlLifecycles",
                columns: new[] { "UserId", "InstrumentId" },
                unique: true,
                filter: "\"ClosedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PnlLifecycles",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "PnlProjection",
                schema: "exchange");
        }
    }
}
