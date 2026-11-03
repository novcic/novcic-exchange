using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Novcic.Core.Migrations
{
    /// <inheritdoc />
    public partial class ProfilesAndKeyProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Avatar",
                schema: "auth",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                schema: "auth",
                table: "Users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                schema: "auth",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SecurityVersion",
                schema: "auth",
                table: "Users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                schema: "auth",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string[]>(
                name: "Products",
                schema: "auth",
                table: "ApiKeys",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.Sql("""
                UPDATE auth."Users" SET "Avatar"='default';
                UPDATE auth."ApiKeys" SET "Products"=ARRAY['Spot','Perpetual','Future','Option'];
                DO $$ DECLARE account RECORD; candidate text; BEGIN
                  FOR account IN SELECT "Id" FROM auth."Users" LOOP
                    LOOP
                      candidate := 'User' || (10000000 + (('x'||substr(replace(gen_random_uuid()::text,'-',''),1,12))::bit(48)::bigint % 90000000))::text;
                      EXIT WHEN NOT EXISTS (SELECT 1 FROM auth."Users" WHERE "NormalizedUsername"=upper(candidate));
                    END LOOP;
                    UPDATE auth."Users" SET "Username"=candidate,"NormalizedUsername"=upper(candidate) WHERE "Id"=account."Id";
                  END LOOP;
                END $$;
                """);
            migrationBuilder.CreateIndex(
                name: "IX_Users_NormalizedUsername",
                schema: "auth",
                table: "Users",
                column: "NormalizedUsername",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_NormalizedUsername",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Avatar",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SecurityVersion",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Username",
                schema: "auth",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Products",
                schema: "auth",
                table: "ApiKeys");
        }
    }
}
