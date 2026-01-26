using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrencyToProductPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices");

            migrationBuilder.AddColumn<string>(
                name: "CurrencyCode",
                table: "ProductPrices",
                type: "text",
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId_ProductWeightId_CurrencyCode",
                table: "ProductPrices",
                columns: new[] { "ProductId", "ProductWeightId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_GrindTypeId",
                table: "CartItems",
                column: "GrindTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_CartItems_RoastLevelId",
                table: "CartItems",
                column: "RoastLevelId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_GrindTypes_GrindTypeId",
                table: "CartItems",
                column: "GrindTypeId",
                principalTable: "GrindTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_RoastLevels_RoastLevelId",
                table: "CartItems",
                column: "RoastLevelId",
                principalTable: "RoastLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_GrindTypes_GrindTypeId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_RoastLevels_RoastLevelId",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_ProductPrices_ProductId_ProductWeightId_CurrencyCode",
                table: "ProductPrices");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_GrindTypeId",
                table: "CartItems");

            migrationBuilder.DropIndex(
                name: "IX_CartItems_RoastLevelId",
                table: "CartItems");

            migrationBuilder.DropColumn(
                name: "CurrencyCode",
                table: "ProductPrices");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices",
                column: "ProductId");
        }
    }
}
