using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorCurrencyHandling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PaymentTransactions");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "PaymentTransactions",
                type: "text",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "OrderItems",
                type: "text",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "Carts",
                type: "text",
                nullable: false,
                defaultValue: "USD");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "Carts");

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PaymentTransactions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
