using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class ExchangeProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InstrumentId",
                schema: "exchange",
                table: "Trades",
                type: "text",
                nullable: false,
                defaultValue: "BTCUSDT");

            migrationBuilder.AddColumn<string>(
                name: "InstrumentId",
                schema: "exchange",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "BTCUSDT");

            migrationBuilder.AddColumn<int>(
                name: "Leverage",
                schema: "exchange",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "ReduceOnly",
                schema: "exchange",
                table: "Orders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ClearingPools",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Cash = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClearingPools", x => x.Id);
                    table.CheckConstraint("clearing_nonnegative", "\"Cash\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "DcaSchedules",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Budget = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    Spent = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Hours = table.Column<int>(type: "integer", nullable: false),
                    NextRun = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Paused = table.Column<bool>(type: "boolean", nullable: false),
                    LastOutcome = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DcaSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FundingEvents",
                schema: "exchange",
                columns: table => new
                {
                    InstrumentId = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Rate = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FundingEvents", x => new { x.InstrumentId, x.Time });
                });

            migrationBuilder.CreateTable(
                name: "IndexSamples",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Spot = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Perpetual = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IndexSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Instruments",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Expiry = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Strike = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    OptionSide = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    SettlementPrice = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Instruments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LendingPools",
                schema: "exchange",
                columns: table => new
                {
                    Asset = table.Column<string>(type: "text", nullable: false),
                    Cash = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Debt = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Reserves = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    ReserveRemainder = table.Column<decimal>(type: "numeric(28,20)", precision: 28, scale: 20, nullable: false),
                    AccruedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LendingPools", x => x.Asset);
                    table.CheckConstraint("lending_nonnegative", "\"Cash\">=0 AND \"Debt\">=0 AND \"Shares\">=0 AND \"Reserves\">=0");
                });

            migrationBuilder.CreateTable(
                name: "LiquidityPools",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Btc = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Usdt = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidityPools", x => x.Id);
                    table.CheckConstraint("amm_nonnegative", "\"Btc\">=0 AND \"Usdt\">=0 AND \"Shares\">=0");
                });

            migrationBuilder.CreateTable(
                name: "LiquidityShares",
                schema: "exchange",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LiquidityShares", x => x.UserId);
                    table.CheckConstraint("lp_nonnegative", "\"Shares\">=0");
                });

            migrationBuilder.CreateTable(
                name: "Loans",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Asset = table.Column<string>(type: "text", nullable: false),
                    CollateralAsset = table.Column<string>(type: "text", nullable: false),
                    Principal = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Interest = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    InterestRemainder = table.Column<decimal>(type: "numeric(28,20)", precision: 28, scale: 20, nullable: false),
                    BadDebt = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Collateral = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loans", x => x.Id);
                    table.CheckConstraint("loan_nonnegative", "\"Principal\">=0 AND \"Interest\">=0 AND \"Collateral\">=0 AND \"BadDebt\">=0");
                });

            migrationBuilder.CreateTable(
                name: "MarginBalances",
                schema: "exchange",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Cash = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarginBalances", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Positions",
                schema: "exchange",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    EntryPrice = table.Column<decimal>(type: "numeric(28,16)", precision: 28, scale: 16, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Leverage = table.Column<int>(type: "integer", nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Reserved = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Positions", x => new { x.UserId, x.InstrumentId });
                });

            migrationBuilder.CreateTable(
                name: "RiskEvents",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false),
                    Time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Supplies",
                schema: "exchange",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Asset = table.Column<string>(type: "text", nullable: false),
                    Shares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    QueuedShares = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Supplies", x => new { x.UserId, x.Asset });
                    table.CheckConstraint("supply_nonnegative", "\"Shares\">=0 AND \"QueuedShares\">=0 AND \"QueuedShares\"<=\"Shares\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_InstrumentId_CreatedAt",
                schema: "exchange",
                table: "Trades",
                columns: new[] { "InstrumentId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_InstrumentId_Status_Side_Price_Sequence",
                schema: "exchange",
                table: "Orders",
                columns: new[] { "InstrumentId", "Status", "Side", "Price", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_DcaSchedules_NextRun",
                schema: "exchange",
                table: "DcaSchedules",
                column: "NextRun");

            migrationBuilder.CreateIndex(
                name: "IX_IndexSamples_Time",
                schema: "exchange",
                table: "IndexSamples",
                column: "Time");

            migrationBuilder.CreateIndex(
                name: "IX_Loans_UserId",
                schema: "exchange",
                table: "Loans",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RiskEvents_UserId_Time",
                schema: "exchange",
                table: "RiskEvents",
                columns: new[] { "UserId", "Time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClearingPools",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "DcaSchedules",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "FundingEvents",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "IndexSamples",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "Instruments",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "LendingPools",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "LiquidityPools",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "LiquidityShares",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "Loans",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "MarginBalances",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "Positions",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "RiskEvents",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "Supplies",
                schema: "exchange");

            migrationBuilder.DropIndex(
                name: "IX_Trades_InstrumentId_CreatedAt",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropIndex(
                name: "IX_Orders_InstrumentId_Status_Side_Price_Sequence",
                schema: "exchange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "InstrumentId",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "InstrumentId",
                schema: "exchange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Leverage",
                schema: "exchange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReduceOnly",
                schema: "exchange",
                table: "Orders");
        }
    }
}
