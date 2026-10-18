using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lynx.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransferFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CounterpartyIban",
                table: "Transactions",
                type: "TEXT",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTransfer",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "TransferLocked",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CounterpartyIban",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "IsTransfer",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "TransferLocked",
                table: "Transactions");
        }
    }
}
