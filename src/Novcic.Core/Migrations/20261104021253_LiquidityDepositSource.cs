using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class LiquidityDepositSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SourceAccount",
                schema: "exchange",
                table: "LiquidityOperations",
                type: "text",
                nullable: false,
                defaultValue: "Unified Trading");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceAccount",
                schema: "exchange",
                table: "LiquidityOperations");
        }
    }
}
