using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderChannelAndShippingMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Channel",
                table: "Orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ShippingMethodName",
                table: "Orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Channel",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingMethodName",
                table: "Orders");
        }
    }
}
