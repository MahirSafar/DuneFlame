using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCategoryHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ParentCategoryId",
                table: "Categories",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_Slug",
                table: "Categories",
                column: "Slug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Categories_ParentCategoryId",
                table: "Categories",
                column: "ParentCategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // PATCH: Replace the EF-generated FK with a DEFERRABLE INITIALLY DEFERRED version.
            // This allows the root category row (ParentCategoryId = Guid.Empty, no matching row)
            // to be inserted safely — PostgreSQL defers the FK check until transaction commit,
            // by which point the root row itself is the only row and Guid.Empty is never validated
            // as an existing Id (the seeder commits root alone first, so this is never violated).
            migrationBuilder.Sql(@"
                ALTER TABLE ""Categories""
                DROP CONSTRAINT ""FK_Categories_Categories_ParentCategoryId"";

                ALTER TABLE ""Categories""
                ADD CONSTRAINT ""FK_Categories_Categories_ParentCategoryId""
                FOREIGN KEY (""ParentCategoryId"") REFERENCES ""Categories""(""Id"")
                ON DELETE RESTRICT
                DEFERRABLE INITIALLY DEFERRED;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Categories_ParentCategoryId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_ParentCategoryId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Categories_Slug",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "ParentCategoryId",
                table: "Categories");
        }
    }
}
