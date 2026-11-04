using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class ClosingFeeAccrual : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "exchange",
                table: "Journal",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Journal_Account_Reason_CreatedAt"
                ON exchange."Journal" ("Account", "Reason", "CreatedAt") WHERE "Reason" IS NOT NULL;
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Journal_Account_Reason_CreatedAt",
                schema: "exchange",
                table: "Journal");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "exchange",
                table: "Journal");
        }
    }
}
