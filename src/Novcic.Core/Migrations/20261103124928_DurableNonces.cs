using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class DurableNonces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Nonce",
                schema: "custody",
                table: "Transfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Sender",
                schema: "custody",
                table: "Transfers",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Nonce",
                schema: "custody",
                table: "Transfers");

            migrationBuilder.DropColumn(
                name: "Sender",
                schema: "custody",
                table: "Transfers");
        }
    }
}
