using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class QuoteValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "QuoteValidatedAt",
                schema: "exchange",
                table: "Orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuoteValidatedAt",
                schema: "exchange",
                table: "Orders");
        }
    }
}
