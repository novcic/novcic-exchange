using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionFeesAndAccountLeverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BuyerFee",
                schema: "exchange",
                table: "Trades",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerFeeAsset",
                schema: "exchange",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerFee",
                schema: "exchange",
                table: "Trades",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerFeeAsset",
                schema: "exchange",
                table: "Trades",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FilledNotional",
                schema: "exchange",
                table: "Orders",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FeesEarnedBtc",
                schema: "exchange",
                table: "LiquidityPools",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "AccountSettings",
                schema: "exchange",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Leverage = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountSettings", x => x.UserId);
                });
            migrationBuilder.Sql("""
                UPDATE exchange."Orders" o SET "FilledNotional" = COALESCE((SELECT SUM(t."Price"*t."Quantity") FROM exchange."Trades" t WHERE t."MakerOrderId"=o."Id" OR t."TakerOrderId"=o."Id"),0);
                INSERT INTO exchange."AccountSettings" ("UserId","Leverage")
                SELECT "UserId",MAX("Leverage") FROM (
                    SELECT p."UserId",p."Leverage" FROM exchange."Positions" p JOIN exchange."Instruments" i ON i."Id"=p."InstrumentId" WHERE p."Quantity"<>0 AND i."Kind" IN ('Perpetual','Future')
                    UNION ALL SELECT o."UserId",o."Leverage" FROM exchange."Orders" o JOIN exchange."Instruments" i ON i."Id"=o."InstrumentId" WHERE o."Status" IN ('Open','PartiallyFilled') AND i."Kind" IN ('Perpetual','Future')
                ) active GROUP BY "UserId";
                UPDATE exchange."Positions" p SET "Leverage"=a."Leverage" FROM exchange."AccountSettings" a,exchange."Instruments" i WHERE p."UserId"=a."UserId" AND i."Id"=p."InstrumentId" AND i."Kind" IN ('Perpetual','Future');
                UPDATE exchange."Orders" o SET "Leverage"=a."Leverage" FROM exchange."AccountSettings" a,exchange."Instruments" i WHERE o."UserId"=a."UserId" AND i."Id"=o."InstrumentId" AND i."Kind" IN ('Perpetual','Future') AND o."Status" IN ('Open','PartiallyFilled');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountSettings",
                schema: "exchange");

            migrationBuilder.DropColumn(
                name: "BuyerFee",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "BuyerFeeAsset",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "SellerFee",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "SellerFeeAsset",
                schema: "exchange",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "FilledNotional",
                schema: "exchange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "FeesEarnedBtc",
                schema: "exchange",
                table: "LiquidityPools");
        }
    }
}
