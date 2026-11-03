using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class IntegratedLiquidity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FeesEarned",
                schema: "exchange",
                table: "LiquidityPools",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "MakerId",
                schema: "exchange",
                table: "LiquidityPools",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Pair",
                schema: "exchange",
                table: "LiquidityPools",
                type: "text",
                nullable: false,
                defaultValue: "BTCUSDT");

            migrationBuilder.CreateTable(
                name: "LiquidityOperations",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PoolId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Asset = table.Column<string>(type: "text", nullable: false),
                    Btc = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Usdt = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Received = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidityOperations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OptionReferences",
                schema: "exchange",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "text", nullable: false),
                    Expiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Strike = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Iv = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OptionReferences", x => x.Symbol);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LiquidityOperations_UserId_CreatedAt",
                schema: "exchange",
                table: "LiquidityOperations",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LiquidityOperations",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "OptionReferences",
                schema: "exchange");

            migrationBuilder.DropColumn(
                name: "FeesEarned",
                schema: "exchange",
                table: "LiquidityPools");

            migrationBuilder.DropColumn(
                name: "MakerId",
                schema: "exchange",
                table: "LiquidityPools");

            migrationBuilder.DropColumn(
                name: "Pair",
                schema: "exchange",
                table: "LiquidityPools");
        }
    }
}
