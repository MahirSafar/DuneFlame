using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVariantEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_ProductPrices_ProductPriceId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_FlavourNotes_Products_ProductId",
                table: "FlavourNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ProductPrices_ProductPriceId",
                table: "OrderItems");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Origins_OriginId",
                table: "Products");

            migrationBuilder.DropTable(
                name: "ProductGrindTypes");

            migrationBuilder.DropTable(
                name: "ProductPrices");

            migrationBuilder.DropTable(
                name: "ProductRoastLevels");

            migrationBuilder.DropTable(
                name: "ProductWeights");

            migrationBuilder.DropIndex(
                name: "IX_Products_OriginId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OriginId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "StockInKg",
                table: "Products");

            migrationBuilder.RenameColumn(
                name: "ProductPriceId",
                table: "OrderItems",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ProductPriceId",
                table: "OrderItems",
                newName: "IX_OrderItems_ProductVariantId");

            migrationBuilder.RenameColumn(
                name: "ProductId",
                table: "FlavourNotes",
                newName: "ProductCoffeeProfileId");

            migrationBuilder.RenameIndex(
                name: "IX_FlavourNotes_ProductId",
                table: "FlavourNotes",
                newName: "IX_FlavourNotes_ProductCoffeeProfileId");

            migrationBuilder.RenameColumn(
                name: "ProductPriceId",
                table: "CartItems",
                newName: "ProductVariantId");

            migrationBuilder.RenameIndex(
                name: "IX_CartItems_ProductPriceId",
                table: "CartItems",
                newName: "IX_CartItems_ProductVariantId");

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SelectedGrindTypeName",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SelectedRoastLevelName",
                table: "OrderItems",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCoffeeCategory",
                table: "Categories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoastLevelId",
                table: "CartItems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "GrindTypeId",
                table: "CartItems",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.CreateTable(
                name: "ProductAttributes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductCoffeeProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCoffeeProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfiles_Origins_OriginId",
                        column: x => x.OriginId,
                        principalTable: "Origins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfiles_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sku = table.Column<string>(type: "text", nullable: false),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    StockQuantity = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariants_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductAttributeValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductAttributeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductAttributeValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductAttributeValues_ProductAttributes_ProductAttributeId",
                        column: x => x.ProductAttributeId,
                        principalTable: "ProductAttributes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCoffeeProfileGrindTypes",
                columns: table => new
                {
                    GrindTypesId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfilesId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCoffeeProfileGrindTypes", x => new { x.GrindTypesId, x.ProfilesId });
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfileGrindTypes_GrindTypes_GrindTypesId",
                        column: x => x.GrindTypesId,
                        principalTable: "GrindTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfileGrindTypes_ProductCoffeeProfiles_Profil~",
                        column: x => x.ProfilesId,
                        principalTable: "ProductCoffeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductCoffeeProfileRoastLevels",
                columns: table => new
                {
                    ProfilesId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoastLevelsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductCoffeeProfileRoastLevels", x => new { x.ProfilesId, x.RoastLevelsId });
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfileRoastLevels_ProductCoffeeProfiles_Profi~",
                        column: x => x.ProfilesId,
                        principalTable: "ProductCoffeeProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductCoffeeProfileRoastLevels_RoastLevels_RoastLevelsId",
                        column: x => x.RoastLevelsId,
                        principalTable: "RoastLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductVariantOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductVariantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductAttributeValueId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductVariantOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductVariantOptions_ProductAttributeValues_ProductAttribu~",
                        column: x => x.ProductAttributeValueId,
                        principalTable: "ProductAttributeValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductVariantOptions_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductAttributeValues_ProductAttributeId",
                table: "ProductAttributeValues",
                column: "ProductAttributeId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCoffeeProfileGrindTypes_ProfilesId",
                table: "ProductCoffeeProfileGrindTypes",
                column: "ProfilesId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCoffeeProfileRoastLevels_RoastLevelsId",
                table: "ProductCoffeeProfileRoastLevels",
                column: "RoastLevelsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCoffeeProfiles_OriginId",
                table: "ProductCoffeeProfiles",
                column: "OriginId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductCoffeeProfiles_ProductId",
                table: "ProductCoffeeProfiles",
                column: "ProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantOptions_ProductAttributeValueId",
                table: "ProductVariantOptions",
                column: "ProductAttributeValueId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariantOptions_ProductVariantId",
                table: "ProductVariantOptions",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_ProductId",
                table: "ProductVariants",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductVariants_Sku",
                table: "ProductVariants",
                column: "Sku",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_ProductVariants_ProductVariantId",
                table: "CartItems",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FlavourNotes_ProductCoffeeProfiles_ProductCoffeeProfileId",
                table: "FlavourNotes",
                column: "ProductCoffeeProfileId",
                principalTable: "ProductCoffeeProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ProductVariants_ProductVariantId",
                table: "OrderItems",
                column: "ProductVariantId",
                principalTable: "ProductVariants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CartItems_ProductVariants_ProductVariantId",
                table: "CartItems");

            migrationBuilder.DropForeignKey(
                name: "FK_FlavourNotes_ProductCoffeeProfiles_ProductCoffeeProfileId",
                table: "FlavourNotes");

            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_ProductVariants_ProductVariantId",
                table: "OrderItems");

            migrationBuilder.DropTable(
                name: "ProductCoffeeProfileGrindTypes");

            migrationBuilder.DropTable(
                name: "ProductCoffeeProfileRoastLevels");

            migrationBuilder.DropTable(
                name: "ProductVariantOptions");

            migrationBuilder.DropTable(
                name: "ProductCoffeeProfiles");

            migrationBuilder.DropTable(
                name: "ProductAttributeValues");

            migrationBuilder.DropTable(
                name: "ProductVariants");

            migrationBuilder.DropTable(
                name: "ProductAttributes");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SelectedGrindTypeName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "SelectedRoastLevelName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsCoffeeCategory",
                table: "Categories");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "OrderItems",
                newName: "ProductPriceId");

            migrationBuilder.RenameIndex(
                name: "IX_OrderItems_ProductVariantId",
                table: "OrderItems",
                newName: "IX_OrderItems_ProductPriceId");

            migrationBuilder.RenameColumn(
                name: "ProductCoffeeProfileId",
                table: "FlavourNotes",
                newName: "ProductId");

            migrationBuilder.RenameIndex(
                name: "IX_FlavourNotes_ProductCoffeeProfileId",
                table: "FlavourNotes",
                newName: "IX_FlavourNotes_ProductId");

            migrationBuilder.RenameColumn(
                name: "ProductVariantId",
                table: "CartItems",
                newName: "ProductPriceId");

            migrationBuilder.RenameIndex(
                name: "IX_CartItems_ProductVariantId",
                table: "CartItems",
                newName: "IX_CartItems_ProductPriceId");

            migrationBuilder.AddColumn<Guid>(
                name: "OriginId",
                table: "Products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StockInKg",
                table: "Products",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<Guid>(
                name: "RoastLevelId",
                table: "CartItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "GrindTypeId",
                table: "CartItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ProductGrindTypes",
                columns: table => new
                {
                    GrindTypesId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductGrindTypes", x => new { x.GrindTypesId, x.ProductsId });
                    table.ForeignKey(
                        name: "FK_ProductGrindTypes_GrindTypes_GrindTypesId",
                        column: x => x.GrindTypesId,
                        principalTable: "GrindTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductGrindTypes_Products_ProductsId",
                        column: x => x.ProductsId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductRoastLevels",
                columns: table => new
                {
                    ProductsId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoastLevelsId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductRoastLevels", x => new { x.ProductsId, x.RoastLevelsId });
                    table.ForeignKey(
                        name: "FK_ProductRoastLevels_Products_ProductsId",
                        column: x => x.ProductsId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProductRoastLevels_RoastLevels_RoastLevelsId",
                        column: x => x.RoastLevelsId,
                        principalTable: "RoastLevels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProductWeights",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Grams = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductWeights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProductPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductWeightId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrencyCode = table.Column<string>(type: "text", nullable: false, defaultValue: "USD"),
                    Price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPrices_ProductWeights_ProductWeightId",
                        column: x => x.ProductWeightId,
                        principalTable: "ProductWeights",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_OriginId",
                table: "Products",
                column: "OriginId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductGrindTypes_ProductsId",
                table: "ProductGrindTypes",
                column: "ProductsId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId_ProductWeightId_CurrencyCode",
                table: "ProductPrices",
                columns: new[] { "ProductId", "ProductWeightId", "CurrencyCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductWeightId",
                table: "ProductPrices",
                column: "ProductWeightId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductRoastLevels_RoastLevelsId",
                table: "ProductRoastLevels",
                column: "RoastLevelsId");

            migrationBuilder.AddForeignKey(
                name: "FK_CartItems_ProductPrices_ProductPriceId",
                table: "CartItems",
                column: "ProductPriceId",
                principalTable: "ProductPrices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FlavourNotes_Products_ProductId",
                table: "FlavourNotes",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_ProductPrices_ProductPriceId",
                table: "OrderItems",
                column: "ProductPriceId",
                principalTable: "ProductPrices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Origins_OriginId",
                table: "Products",
                column: "OriginId",
                principalTable: "Origins",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
