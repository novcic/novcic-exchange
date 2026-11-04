using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class LiquidityPerformanceAndClosingBackoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RetryDelayMs",
                schema: "exchange",
                table: "PositionProtections",
                type: "integer",
                nullable: false,
                defaultValue: 100);

            migrationBuilder.AddColumn<decimal>(
                name: "DepositValueUsd",
                schema: "exchange",
                table: "LiquidityOperations",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WithdrawalValueUsd",
                schema: "exchange",
                table: "LiquidityOperations",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            // Reconstruct historical USD estimates from the reference available
            // at the event, never from today's BTC price. Missing history stays null.
            migrationBuilder.Sql("""
                UPDATE exchange."LiquidityOperations" o
                SET "DepositValueUsd" = CASE WHEN o."Btc"=0 THEN o."Usdt" ELSE
                    trunc(o."Usdt" + o."Btc" * (SELECT s."Price" FROM exchange."IndexSamples" s
                        WHERE s."Time"<=o."CreatedAt" AND s."Time">=o."CreatedAt"-interval '5 seconds'
                        ORDER BY s."Time" DESC,s."Id" DESC LIMIT 1),8) END
                WHERE o."Kind"='Deposit';

                WITH payouts AS (
                    SELECT o."Id",o."CompletedAt",
                        CASE WHEN o."Asset"='USDT' THEN o."Received" ELSE 0 END AS usd,
                        CASE WHEN o."Asset"='BTC' THEN o."Received" ELSE 0 END +
                        COALESCE((SELECT sum(l."AvailableDelta"+l."LockedDelta") FROM exchange."Ledger" l
                            WHERE l."UserId"=o."UserId" AND l."ReferenceId"=o."Id" AND l."Asset"='BTC'
                            AND l."Reason"='Liquidity withdrawal dust'),0) AS btc
                    FROM exchange."LiquidityOperations" o WHERE o."Kind"='Withdrawal' AND o."Status"='Completed'
                )
                UPDATE exchange."LiquidityOperations" o SET "WithdrawalValueUsd" = CASE WHEN p.btc=0 THEN p.usd ELSE
                    trunc(p.usd+p.btc*(SELECT s."Price" FROM exchange."IndexSamples" s
                        WHERE s."Time"<=p."CompletedAt" AND s."Time">=p."CompletedAt"-interval '5 seconds'
                        ORDER BY s."Time" DESC,s."Id" DESC LIMIT 1),8) END
                FROM payouts p WHERE o."Id"=p."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RetryDelayMs",
                schema: "exchange",
                table: "PositionProtections");

            migrationBuilder.DropColumn(
                name: "DepositValueUsd",
                schema: "exchange",
                table: "LiquidityOperations");

            migrationBuilder.DropColumn(
                name: "WithdrawalValueUsd",
                schema: "exchange",
                table: "LiquidityOperations");
        }
    }
}
