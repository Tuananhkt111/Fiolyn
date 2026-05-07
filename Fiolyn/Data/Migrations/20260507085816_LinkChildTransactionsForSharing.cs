using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkChildTransactionsForSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ParentTransactionId",
                table: "Transaction",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transaction_ParentTransactionId",
                table: "Transaction",
                column: "ParentTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Transaction_Transaction_ParentTransactionId",
                table: "Transaction",
                column: "ParentTransactionId",
                principalTable: "Transaction",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Transaction_Transaction_ParentTransactionId",
                table: "Transaction");

            migrationBuilder.DropIndex(
                name: "IX_Transaction_ParentTransactionId",
                table: "Transaction");

            migrationBuilder.DropColumn(
                name: "ParentTransactionId",
                table: "Transaction");
        }
    }
}
