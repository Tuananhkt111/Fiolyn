using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class RequireBrokerAccountSpendAccountId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount");

            // No defaultValue: rows must be backfilled with a real SpendAccount.Id before this
            // migration is applied. If any SpendAccountId is NULL the ALTER will fail with 23502,
            // which is the desired loud signal that backfill was skipped.
            migrationBuilder.AlterColumn<long>(
                name: "SpendAccountId",
                table: "BrokerAccount",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount",
                column: "SpendAccountId",
                principalTable: "SpendAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount");

            migrationBuilder.AlterColumn<long>(
                name: "SpendAccountId",
                table: "BrokerAccount",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddForeignKey(
                name: "FK_BrokerAccount_SpendAccount_SpendAccountId",
                table: "BrokerAccount",
                column: "SpendAccountId",
                principalTable: "SpendAccount",
                principalColumn: "Id");
        }
    }
}
