using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class OrderClosingHistoryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Allow an online repair before deployment; building this index must
            // not block inserts into a busy exchange's order table.
            migrationBuilder.Sql("""
                CREATE INDEX CONCURRENTLY IF NOT EXISTS "IX_Orders_ClosingJobId_Sequence"
                ON exchange."Orders" ("ClosingJobId", "Sequence")
                WHERE "ClosingJobId" IS NOT NULL;
                """, suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_ClosingJobId_Sequence",
                schema: "exchange",
                table: "Orders");
        }
    }
}
