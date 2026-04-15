using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Fiolyn.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBrokerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Broker",
                table: "StockTransaction");

            migrationBuilder.AddColumn<long>(
                name: "BrokerAccountId",
                table: "StockTransaction",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "BrokerAccount",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Created = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastModified = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrokerAccount", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransaction_BrokerAccountId",
                table: "StockTransaction",
                column: "BrokerAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransaction_BrokerAccount_BrokerAccountId",
                table: "StockTransaction",
                column: "BrokerAccountId",
                principalTable: "BrokerAccount",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockTransaction_BrokerAccount_BrokerAccountId",
                table: "StockTransaction");

            migrationBuilder.DropTable(
                name: "BrokerAccount");

            migrationBuilder.DropIndex(
                name: "IX_StockTransaction_BrokerAccountId",
                table: "StockTransaction");

            migrationBuilder.DropColumn(
                name: "BrokerAccountId",
                table: "StockTransaction");

            migrationBuilder.AddColumn<string>(
                name: "Broker",
                table: "StockTransaction",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");
        }
    }
}
