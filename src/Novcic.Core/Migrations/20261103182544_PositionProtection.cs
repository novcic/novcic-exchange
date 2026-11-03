using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class PositionProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AnalyticsReady",
                schema: "exchange",
                table: "Positions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "LifecycleId",
                schema: "exchange",
                table: "Positions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<decimal>(
                name: "TradeRealizedPnl",
                schema: "exchange",
                table: "Positions",
                type: "numeric(28,8)",
                precision: 28,
                scale: 8,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "exchange",
                table: "Positions",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);

            migrationBuilder.Sql("UPDATE exchange.\"Positions\" SET \"LifecycleId\"=gen_random_uuid()");

            migrationBuilder.AddColumn<Guid>(
                name: "ClosingJobId",
                schema: "exchange",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesId",
                schema: "exchange",
                table: "Orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PositionProtections",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstrumentId = table.Column<string>(type: "text", nullable: false),
                    LifecycleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Direction = table.Column<int>(type: "integer", nullable: false),
                    TakeProfit = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    StopLoss = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TriggerLeg = table.Column<string>(type: "text", nullable: true),
                    TriggerSequence = table.Column<long>(type: "bigint", nullable: true),
                    Attempts = table.Column<long>(type: "bigint", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PositionProtections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PositionProtections_Status_NextAttemptAt",
                schema: "exchange",
                table: "PositionProtections",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PositionProtections_UserId_InstrumentId_LifecycleId",
                schema: "exchange",
                table: "PositionProtections",
                columns: new[] { "UserId", "InstrumentId", "LifecycleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PositionProtections",
                schema: "exchange");

            migrationBuilder.DropColumn(
                name: "AnalyticsReady",
                schema: "exchange",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "LifecycleId",
                schema: "exchange",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "TradeRealizedPnl",
                schema: "exchange",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "exchange",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "ClosingJobId",
                schema: "exchange",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReplacesId",
                schema: "exchange",
                table: "Orders");
        }
    }
}
