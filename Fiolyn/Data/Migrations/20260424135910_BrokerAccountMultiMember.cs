using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class BrokerAccountMultiMember : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BrokerAccountMember",
                columns: table => new
                {
                    BrokerAccountId = table.Column<long>(type: "bigint", nullable: false),
                    AppUserId = table.Column<long>(type: "bigint", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerAccountMember", x => new { x.BrokerAccountId, x.AppUserId });
                    table.ForeignKey(
                        name: "FK_BrokerAccountMember_AppUser_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrokerAccountMember_BrokerAccount_BrokerAccountId",
                        column: x => x.BrokerAccountId,
                        principalTable: "BrokerAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrokerAccountMember_AppUserId_BrokerAccountId",
                table: "BrokerAccountMember",
                columns: new[] { "AppUserId", "BrokerAccountId" });

            // Backfill: every existing active BrokerAccount is assigned to the first (lowest-Id) active
            // admin as its sole member. Fails loudly if no active admin exists, rather than silently
            // orphaning the data.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE admin_id bigint ;
                BEGIN
                  SELECT ""Id"" INTO admin_id
                  FROM ""AppUser""
                  WHERE ""IsAdmin"" = TRUE AND ""IsDeleted"" = FALSE
                  ORDER BY ""Id""
                  LIMIT 1 ;

                  IF admin_id IS NULL THEN
                    RAISE EXCEPTION 'BrokerAccountMultiMember migration requires at least one active admin AppUser.' ;
                  END IF ;

                  INSERT INTO ""BrokerAccountMember"" (""BrokerAccountId"", ""AppUserId"", ""JoinedAt"")
                  SELECT ""Id"", admin_id, ""Created""
                  FROM ""BrokerAccount""
                  WHERE ""IsDeleted"" = FALSE ;
                END $$ ;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrokerAccountMember");
        }
    }
}
