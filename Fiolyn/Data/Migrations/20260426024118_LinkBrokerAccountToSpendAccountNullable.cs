using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class LinkBrokerAccountToSpendAccountNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "SpendAccountId",
                table: "BrokerAccount",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BrokerAccount_SpendAccountId",
                table: "BrokerAccount",
                column: "SpendAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount",
                column: "SpendAccountId",
                principalTable: "SpendAccount",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount");

            migrationBuilder.DropIndex(
                name: "IX_BrokerAccount_SpendAccountId",
                table: "BrokerAccount");

            migrationBuilder.DropColumn(
                name: "SpendAccountId",
                table: "BrokerAccount");
        }
    }
}
