using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuneFlame.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorInquiryTypeToEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop the text DEFAULT so PostgreSQL can freely alter the column type.
            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" DROP DEFAULT;
                """);

            // 2. Convert the text column to integer using an explicit USING expression.
            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" TYPE integer
                USING CASE "InquiryType"
                    WHEN 'GeneralInquiry'  THEN 0
                    WHEN 'General Inquiry' THEN 0
                    WHEN 'OrderIssue'      THEN 1
                    WHEN 'Order Issue'     THEN 1
                    WHEN 'Wholesale'       THEN 2
                    WHEN 'Support'         THEN 3
                    ELSE 0
                END;
                """);

            // 3. Restore an integer default (0 = GeneralInquiry).
            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" SET DEFAULT 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" DROP DEFAULT;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" TYPE text
                USING CASE "InquiryType"
                    WHEN 0 THEN 'GeneralInquiry'
                    WHEN 1 THEN 'OrderIssue'
                    WHEN 2 THEN 'Wholesale'
                    WHEN 3 THEN 'Support'
                    ELSE 'GeneralInquiry'
                END;
                """);

            migrationBuilder.Sql("""
                ALTER TABLE "ContactMessages"
                ALTER COLUMN "InquiryType" SET DEFAULT '';
                """);
        }
    }
}
