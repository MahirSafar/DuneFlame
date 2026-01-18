using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSlugToProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Slug",
                table: "Products",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            // Generate slugs from product names
            migrationBuilder.Sql(@"
                UPDATE ""Products"" 
                SET ""Slug"" = LOWER(
                    REGEXP_REPLACE(
                        REGEXP_REPLACE(""Name"", '[^a-z0-9\s-]', '', 'gi'),
                        '\s+', '-', 'g'
                    )
                )
                WHERE ""Slug"" = '';
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Slug",
                table: "Products",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Slug",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Slug",
                table: "Products");
        }
    }
}
