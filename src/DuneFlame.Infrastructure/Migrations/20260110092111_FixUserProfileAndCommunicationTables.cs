using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixUserProfileAndCommunicationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfile_Users_UserId",
                table: "UserProfile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfile",
                table: "UserProfile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NewsletterSubscription",
                table: "NewsletterSubscription");

            migrationBuilder.RenameTable(
                name: "UserProfile",
                newName: "UserProfiles");

            migrationBuilder.RenameTable(
                name: "NewsletterSubscription",
                newName: "NewsletterSubscriptions");

            migrationBuilder.RenameIndex(
                name: "IX_UserProfile_UserId",
                table: "UserProfiles",
                newName: "IX_UserProfiles_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_NewsletterSubscription_Email",
                table: "NewsletterSubscriptions",
                newName: "IX_NewsletterSubscriptions_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfiles",
                table: "UserProfiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NewsletterSubscriptions",
                table: "NewsletterSubscriptions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ContactMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Subject = table.Column<string>(type: "text", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    IsRead = table.Column<bool>(type: "boolean", nullable: false),
                    AdminReply = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContactMessages", x => x.Id);
                });

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfiles_Users_UserId",
                table: "UserProfiles",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserProfiles_Users_UserId",
                table: "UserProfiles");

            migrationBuilder.DropTable(
                name: "ContactMessages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_UserProfiles",
                table: "UserProfiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_NewsletterSubscriptions",
                table: "NewsletterSubscriptions");

            migrationBuilder.RenameTable(
                name: "UserProfiles",
                newName: "UserProfile");

            migrationBuilder.RenameTable(
                name: "NewsletterSubscriptions",
                newName: "NewsletterSubscription");

            migrationBuilder.RenameIndex(
                name: "IX_UserProfiles_UserId",
                table: "UserProfile",
                newName: "IX_UserProfile_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_NewsletterSubscriptions_Email",
                table: "NewsletterSubscription",
                newName: "IX_NewsletterSubscription_Email");

            migrationBuilder.AddPrimaryKey(
                name: "PK_UserProfile",
                table: "UserProfile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_NewsletterSubscription",
                table: "NewsletterSubscription",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_UserProfile_Users_UserId",
                table: "UserProfile",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
