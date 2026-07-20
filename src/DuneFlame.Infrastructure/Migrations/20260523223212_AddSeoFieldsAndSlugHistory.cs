using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSeoFieldsAndSlugHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MetaDescription",
                table: "ProductTranslations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaTitle",
                table: "ProductTranslations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AltText",
                table: "ProductImages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProductSlugHistories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldSlug = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductSlugHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductSlugHistories_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductSlugHistories_ProductId",
                table: "ProductSlugHistories",
                column: "ProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductSlugHistories");

            migrationBuilder.DropColumn(
                name: "MetaDescription",
                table: "ProductTranslations");

            migrationBuilder.DropColumn(
                name: "MetaTitle",
                table: "ProductTranslations");

            migrationBuilder.DropColumn(
                name: "AltText",
                table: "ProductImages");
        }
    }
}
