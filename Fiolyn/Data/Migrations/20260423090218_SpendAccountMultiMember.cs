using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class SpendAccountMultiMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SpendAccountMember",
                columns: table => new
                {
                    SpendAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AppUserId = table.Column<long>(type: "bigint", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpendAccountMember", x => new { x.SpendAccountId, x.AppUserId });
                    table.ForeignKey(
                        name: "FK_SpendAccountMember_AppUser_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SpendAccountMember_SpendAccount_SpendAccountId",
                        column: x => x.SpendAccountId,
                        principalTable: "SpendAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpendAccountMember_AppUserId_SpendAccountId",
                table: "SpendAccountMember",
                columns: new[] { "AppUserId", "SpendAccountId" });

            // Backfill: every existing single-owner SpendAccount becomes a one-member account.
            migrationBuilder.Sql(@"
                INSERT INTO ""SpendAccountMember"" (""SpendAccountId"", ""AppUserId"", ""JoinedAt"")
                SELECT ""Id"", ""AppUserId"", ""Created""
                FROM ""SpendAccount"";
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_SpendAccount_AppUser_AppUserId",
                table: "SpendAccount");

            migrationBuilder.DropIndex(
                name: "IX_SpendAccount_AppUserId",
                table: "SpendAccount");

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "SpendAccount");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "AppUserId",
                table: "SpendAccount",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Lossy backfill: pick one member per account to become the single owner.
            migrationBuilder.Sql(@"
                UPDATE ""SpendAccount"" sa
                SET ""AppUserId"" = sub.""AppUserId""
                FROM (
                    SELECT DISTINCT ON (""SpendAccountId"") ""SpendAccountId"", ""AppUserId""
                    FROM ""SpendAccountMember""
                    ORDER BY ""SpendAccountId"", ""JoinedAt""
                ) sub
                WHERE sa.""Id"" = sub.""SpendAccountId"";
            ");

            migrationBuilder.CreateIndex(
                name: "IX_SpendAccount_AppUserId",
                table: "SpendAccount",
                column: "AppUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_SpendAccount_AppUser_AppUserId",
                table: "SpendAccount",
                column: "AppUserId",
                principalTable: "AppUser",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "SpendAccountMember");
        }
    }
}
