using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionHierarchyAndMigrateStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Step 1 — Create the new lookup and base tables.
            migrationBuilder.CreateTable(
                name: "TransactionCategory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Transaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SpendAccountId = table.Column<long>(type: "bigint", nullable: false),
                    CategoryId = table.Column<long>(type: "bigint", nullable: true),
                    Date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PayerId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<long>(type: "bigint", nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transaction_AppUser_PayerId",
                        column: x => x.PayerId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Transaction_SpendAccount_SpendAccountId",
                        column: x => x.SpendAccountId,
                        principalTable: "SpendAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transaction_TransactionCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "TransactionCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Step 2 — Backfill base Transaction rows from existing StockTransaction data.
            // TransactionType=0 is Buy → negative Amount; Sell (1) and Dividend (2) → positive.
            // PayerId stays NULL for legacy rows since they predate the column.
            migrationBuilder.Sql(@"
                INSERT INTO ""Transaction""
                    ( ""Id"", ""SpendAccountId"", ""CategoryId"", ""Date"", ""Description"",
                      ""PayerId"", ""Amount"", ""Created"", ""LastModified"", ""IsDeleted"" )
                SELECT st.""Id"", ba.""SpendAccountId"", NULL,
                       st.""TransactionDate"", st.""Notes"", NULL,
                       CASE WHEN st.""TransactionType"" = 0 THEN -st.""TotalAmount""
                            ELSE st.""TotalAmount""
                       END,
                       st.""Created"", st.""LastModified"", st.""IsDeleted""
                FROM ""StockTransaction"" st
                JOIN ""BrokerAccount"" ba ON st.""BrokerAccountId"" = ba.""Id"" ;
            ");

            // Step 3 — Now safe to drop the columns whose data has been moved into Transaction.
            // TotalAmount is fully derivable from Math.Abs(Transaction.Amount) — no separate column.
            migrationBuilder.DropColumn(
                name: "Created",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "LastModified",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "TotalAmount",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "TransactionDate",
                table: "StockTransaction");

            migrationBuilder.RenameColumn(
                name: "TransactionType",
                table: "StockTransaction",
                newName: "StockTransactionType");

            // Step 4 — Drop the auto-increment identity on StockTransaction.Id; under TPT the base
            // table now owns identity generation for the shared key.
            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "StockTransaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            // Step 5 — Create the leaf tables (each shares the Transaction PK under TPT).
            migrationBuilder.CreateTable(
                name: "AdjustmentTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdjustmentTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdjustmentTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BorrowingTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorrowingTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BorrowingTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExpenseTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExpenseTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "IncomeTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncomeTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncomeTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LendTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    CounterpartyName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LendTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LendTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    DestinationSpendAccountId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferTransaction", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferTransaction_SpendAccount_DestinationSpendAccountId",
                        column: x => x.DestinationSpendAccountId,
                        principalTable: "SpendAccount",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransferTransaction_Transaction_Id",
                        column: x => x.Id,
                        principalTable: "Transaction",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Step 6 — Indexes, then attach StockTransaction to the TPT base.
            migrationBuilder.CreateIndex(
                name: "IX_Transaction_CategoryId",
                table: "Transaction",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_PayerId",
                table: "Transaction",
                column: "PayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_SpendAccountId",
                table: "Transaction",
                column: "SpendAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionCategory_Kind_Name",
                table: "TransactionCategory",
                columns: new[] { "Kind", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferTransaction_DestinationSpendAccountId",
                table: "TransferTransaction",
                column: "DestinationSpendAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransaction_Transaction_Id",
                table: "StockTransaction",
                column: "Id",
                principalTable: "Transaction",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Step 7 — Bump the shared identity sequence past the migrated stock ids so future
            // inserts on any leaf draw new ids without colliding with the back-filled rows.
            migrationBuilder.Sql(@"
                SELECT setval(
                    pg_get_serial_sequence('""Transaction""', 'Id'),
                    GREATEST(
                        COALESCE((SELECT MAX(""Id"") FROM ""Transaction""), 0),
                        1 ));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransaction_Transaction_Id",
                table: "StockTransaction");

            migrationBuilder.DropTable(
                name: "AdjustmentTransaction");

            migrationBuilder.DropTable(
                name: "BorrowingTransaction");

            migrationBuilder.DropTable(
                name: "ExpenseTransaction");

            migrationBuilder.DropTable(
                name: "IncomeTransaction");

            migrationBuilder.DropTable(
                name: "LendTransaction");

            migrationBuilder.DropTable(
                name: "TransferTransaction");

            migrationBuilder.RenameColumn(
                name: "StockTransactionType",
                table: "StockTransaction",
                newName: "TransactionType");

            migrationBuilder.AlterColumn<long>(
                name: "Id",
                table: "StockTransaction",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<DateTime>(
                name: "Created",
                table: "StockTransaction",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "StockTransaction",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModified",
                table: "StockTransaction",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "StockTransaction",
                type: "character varying(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "TotalAmount",
                table: "StockTransaction",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "TransactionDate",
                table: "StockTransaction",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Backfill the dropped columns from the soon-to-be-dropped Transaction rows.
            // TotalAmount = ABS(Amount) — sign was carried by Transaction.Amount on the Up path.
            migrationBuilder.Sql(@"
                UPDATE ""StockTransaction"" st
                SET ""TransactionDate"" = t.""Date"",
                    ""Notes"" = t.""Description"",
                    ""TotalAmount"" = ABS(t.""Amount""),
                    ""Created"" = t.""Created"",
                    ""LastModified"" = t.""LastModified"",
                    ""IsDeleted"" = t.""IsDeleted""
                FROM ""Transaction"" t
                WHERE t.""Id"" = st.""Id"" ;
            ");

            migrationBuilder.DropTable(
                name: "Transaction");

            migrationBuilder.DropTable(
                name: "TransactionCategory");
        }
    }
}
