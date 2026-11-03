using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class CustodyAndJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "custody");

            migrationBuilder.CreateTable(
                name: "Addresses",
                schema: "custody",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    EncryptedKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Addresses", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Cursors",
                schema: "custody",
                columns: table => new
                {
                    Generation = table.Column<string>(type: "text", nullable: false),
                    Block = table.Column<long>(type: "bigint", nullable: false),
                    BlockHash = table.Column<string>(type: "text", nullable: false),
                    Quarantined = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cursors", x => x.Generation);
                });

            migrationBuilder.CreateTable(
                name: "Journal",
                schema: "exchange",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Account = table.Column<string>(type: "text", nullable: false),
                    Asset = table.Column<string>(type: "text", nullable: false),
                    Delta = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Journal", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transfers",
                schema: "custody",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Generation = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Asset = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: true),
                    RawTransaction = table.Column<string>(type: "text", nullable: true),
                    Block = table.Column<long>(type: "bigint", nullable: true),
                    BlockHash = table.Column<string>(type: "text", nullable: true),
                    LogIndex = table.Column<long>(type: "bigint", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transfers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Addresses_Address",
                schema: "custody",
                table: "Addresses",
                column: "Address",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Journal_ReferenceId_Asset",
                schema: "exchange",
                table: "Journal",
                columns: new[] { "ReferenceId", "Asset" });

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_Generation_Hash_LogIndex_Kind",
                schema: "custody",
                table: "Transfers",
                columns: new[] { "Generation", "Hash", "LogIndex", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transfers_UserId_CreatedAt",
                schema: "custody",
                table: "Transfers",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Addresses",
                schema: "custody");

            migrationBuilder.DropTable(
                name: "Cursors",
                schema: "custody");

            migrationBuilder.DropTable(
                name: "Journal",
                schema: "exchange");

            migrationBuilder.DropTable(
                name: "Transfers",
                schema: "custody");
        }
    }
}
