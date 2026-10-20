using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RedactSensitivePostmasterSubjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [SentMessages]
                SET [Subject] = N'[redacted]'
                WHERE [TemplateKey] IN (N'auth.otp_code', N'onboarding.otp_code', N'sig.verification-challenge.v1');
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Redacted secrets cannot be restored.
        }
    }
}
