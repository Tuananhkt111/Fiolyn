using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameStockTransactionsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockTransactions",
                table: "StockTransactions");

            migrationBuilder.RenameTable(
                name: "StockTransactions",
                newName: "StockTransaction");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockTransaction",
                table: "StockTransaction",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockTransaction",
                table: "StockTransaction");

            migrationBuilder.RenameTable(
                name: "StockTransaction",
                newName: "StockTransactions");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockTransactions",
                table: "StockTransactions",
                column: "Id");
        }
    }
}
