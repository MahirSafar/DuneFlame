using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIsActiveFromProducts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsActive",
                table: "Products",
                newName: "IsDeleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "IsDeleted",
                table: "Products",
                newName: "IsActive");
        }
    }
}
