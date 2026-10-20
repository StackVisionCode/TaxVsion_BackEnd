using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Sms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RedactSensitiveSmsBodies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [smsMessages]
                SET [Body] = N'[redacted]'
                WHERE [SourceContext] LIKE N'notification:sms%';
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
