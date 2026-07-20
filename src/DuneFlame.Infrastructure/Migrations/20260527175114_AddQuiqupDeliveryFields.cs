using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuiqupDeliveryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "QuiqupOrderId",
                table: "Orders",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuiqupTrackingUrl",
                table: "Orders",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuiqupOrderId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "QuiqupTrackingUrl",
                table: "Orders");
        }
    }
}
