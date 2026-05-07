using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCounterparty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Counterparty",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppUserId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counterparty", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Counterparty_AppUser_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Counterparty_AppUserId",
                table: "Counterparty",
                column: "AppUserId");

            // Per-user case-insensitive uniqueness via expression index. EF can't model LOWER() in
            // HasIndex, so we add it as raw SQL — the model snapshot won't reflect it.
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_Counterparty_AppUserId_LowerName""
                ON ""Counterparty"" (""AppUserId"", LOWER(""Name""));
            ");

            migrationBuilder.AddColumn<long>(
                name: "CounterpartyId",
                table: "LendTransaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CounterpartyId",
                table: "BorrowingTransaction",
                type: "bigint",
                nullable: true);

            // Backfill: for each existing lend/borrow row, derive an owner = COALESCE(PayerId,
            // first SpendAccountMember by JoinedAt). Insert one Counterparty per distinct
            // (owner, LOWER(TRIM(name))) deduped across BOTH leaves, then point each row at the
            // matching counterparty.
            migrationBuilder.Sql(@"
                WITH source AS (
                    SELECT lt.""Id"" AS tx_id,
                           'lend'::text AS tx_kind,
                           COALESCE(b.""PayerId"",
                                    (SELECT m.""AppUserId""
                                     FROM ""SpendAccountMember"" m
                                     WHERE m.""SpendAccountId"" = b.""SpendAccountId""
                                     ORDER BY m.""JoinedAt"" ASC
                                     LIMIT 1)) AS owner_id,
                           TRIM(lt.""CounterpartyName"") AS name_trimmed
                    FROM ""LendTransaction"" lt
                    JOIN ""Transaction"" b ON b.""Id"" = lt.""Id""
                    WHERE lt.""CounterpartyName"" IS NOT NULL
                      AND length(TRIM(lt.""CounterpartyName"")) > 0
                    UNION ALL
                    SELECT bt.""Id"" AS tx_id,
                           'borrow'::text AS tx_kind,
                           COALESCE(b.""PayerId"",
                                    (SELECT m.""AppUserId""
                                     FROM ""SpendAccountMember"" m
                                     WHERE m.""SpendAccountId"" = b.""SpendAccountId""
                                     ORDER BY m.""JoinedAt"" ASC
                                     LIMIT 1)) AS owner_id,
                           TRIM(bt.""CounterpartyName"") AS name_trimmed
                    FROM ""BorrowingTransaction"" bt
                    JOIN ""Transaction"" b ON b.""Id"" = bt.""Id""
                    WHERE bt.""CounterpartyName"" IS NOT NULL
                      AND length(TRIM(bt.""CounterpartyName"")) > 0
                ),
                inserted AS (
                    INSERT INTO ""Counterparty"" (""AppUserId"", ""Name"", ""Created"", ""LastModified"", ""IsDeleted"")
                    SELECT DISTINCT ON (owner_id, LOWER(name_trimmed))
                           owner_id, name_trimmed, now(), now(), false
                    FROM source
                    WHERE owner_id IS NOT NULL
                    ORDER BY owner_id, LOWER(name_trimmed), name_trimmed
                    RETURNING ""Id"", ""AppUserId"", ""Name""
                )
                SELECT 1;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""LendTransaction"" lt
                SET ""CounterpartyId"" = c.""Id""
                FROM ""Counterparty"" c
                JOIN ""Transaction"" b ON b.""Id"" = lt.""Id""
                WHERE c.""AppUserId"" = COALESCE(b.""PayerId"",
                                                  (SELECT m.""AppUserId""
                                                   FROM ""SpendAccountMember"" m
                                                   WHERE m.""SpendAccountId"" = b.""SpendAccountId""
                                                   ORDER BY m.""JoinedAt"" ASC
                                                   LIMIT 1))
                  AND LOWER(c.""Name"") = LOWER(TRIM(lt.""CounterpartyName""))
                  AND lt.""CounterpartyId"" IS NULL
                  AND lt.""CounterpartyName"" IS NOT NULL
                  AND length(TRIM(lt.""CounterpartyName"")) > 0;
            ");

            migrationBuilder.Sql(@"
                UPDATE ""BorrowingTransaction"" bt
                SET ""CounterpartyId"" = c.""Id""
                FROM ""Counterparty"" c
                JOIN ""Transaction"" b ON b.""Id"" = bt.""Id""
                WHERE c.""AppUserId"" = COALESCE(b.""PayerId"",
                                                  (SELECT m.""AppUserId""
                                                   FROM ""SpendAccountMember"" m
                                                   WHERE m.""SpendAccountId"" = b.""SpendAccountId""
                                                   ORDER BY m.""JoinedAt"" ASC
                                                   LIMIT 1))
                  AND LOWER(c.""Name"") = LOWER(TRIM(bt.""CounterpartyName""))
                  AND bt.""CounterpartyId"" IS NULL
                  AND bt.""CounterpartyName"" IS NOT NULL
                  AND length(TRIM(bt.""CounterpartyName"")) > 0;
            ");

            // Pre-flight: every surviving lend/borrow row must have a non-null CounterpartyId
            // before we can promote the column to NOT NULL. The only reason this would still be
            // NULL is if a row's SpendAccount has zero members AND PayerId is NULL — shouldn't
            // happen in practice (last-member removal is blocked at the API), but better to fail
            // loudly here than to lose data.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    orphan_count bigint;
                BEGIN
                    SELECT COUNT(*) INTO orphan_count
                    FROM (
                        SELECT 1 FROM ""LendTransaction"" WHERE ""CounterpartyId"" IS NULL
                        UNION ALL
                        SELECT 1 FROM ""BorrowingTransaction"" WHERE ""CounterpartyId"" IS NULL
                    ) o;
                    IF orphan_count > 0 THEN
                        RAISE EXCEPTION 'Backfill left % lend/borrow row(s) without a Counterparty — manual repair required before completing the migration.', orphan_count;
                    END IF;
                END $$;
            ");

            migrationBuilder.AlterColumn<long>(
                name: "CounterpartyId",
                table: "LendTransaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AlterColumn<long>(
                name: "CounterpartyId",
                table: "BorrowingTransaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LendTransaction_CounterpartyId",
                table: "LendTransaction",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowingTransaction_CounterpartyId",
                table: "BorrowingTransaction",
                column: "CounterpartyId");

            migrationBuilder.AddForeignKey(
                name: "FK_BorrowingTransaction_Counterparty_CounterpartyId",
                table: "BorrowingTransaction",
                column: "CounterpartyId",
                principalTable: "Counterparty",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LendTransaction_Counterparty_CounterpartyId",
                table: "LendTransaction",
                column: "CounterpartyId",
                principalTable: "Counterparty",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "CounterpartyName",
                table: "LendTransaction");

            migrationBuilder.DropColumn(
                name: "CounterpartyName",
                table: "BorrowingTransaction");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-add the free-text column nullable so we can populate from Counterparty before
            // tightening to NOT NULL.
            migrationBuilder.AddColumn<string>(
                name: "CounterpartyName",
                table: "LendTransaction",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CounterpartyName",
                table: "BorrowingTransaction",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE ""LendTransaction"" lt
                SET ""CounterpartyName"" = c.""Name""
                FROM ""Counterparty"" c
                WHERE c.""Id"" = lt.""CounterpartyId"";
            ");

            migrationBuilder.Sql(@"
                UPDATE ""BorrowingTransaction"" bt
                SET ""CounterpartyName"" = c.""Name""
                FROM ""Counterparty"" c
                WHERE c.""Id"" = bt.""CounterpartyId"";
            ");

            migrationBuilder.AlterColumn<string>(
                name: "CounterpartyName",
                table: "LendTransaction",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CounterpartyName",
                table: "BorrowingTransaction",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_BorrowingTransaction_Counterparty_CounterpartyId",
                table: "BorrowingTransaction");

            migrationBuilder.DropForeignKey(
                name: "FK_LendTransaction_Counterparty_CounterpartyId",
                table: "LendTransaction");

            migrationBuilder.DropIndex(
                name: "IX_LendTransaction_CounterpartyId",
                table: "LendTransaction");

            migrationBuilder.DropIndex(
                name: "IX_BorrowingTransaction_CounterpartyId",
                table: "BorrowingTransaction");

            migrationBuilder.DropColumn(
                name: "CounterpartyId",
                table: "LendTransaction");

            migrationBuilder.DropColumn(
                name: "CounterpartyId",
                table: "BorrowingTransaction");

            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Counterparty_AppUserId_LowerName"";");

            migrationBuilder.DropTable(
                name: "Counterparty");
        }
    }
}
