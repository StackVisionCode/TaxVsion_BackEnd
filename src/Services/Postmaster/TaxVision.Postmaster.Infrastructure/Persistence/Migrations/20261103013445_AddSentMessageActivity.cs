using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSentMessageActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Activity",
                table: "SentMessages",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Sent"
            );

            // Relleno con la misma regla que SentMessage.RefreshActivity: estado del envío y, si salió,
            // el peor outcome (queja > rebote/rechazo > demorado > aceptado > entregado).
            migrationBuilder.Sql(
                """
                UPDATE m SET [Activity] =
                    CASE
                        WHEN m.[Status] IN (N'Queued', N'Sending') THEN N'Sending'
                        WHEN m.[Status] IN (N'Failed', N'ProviderNotConfigured') THEN N'Failed'
                        WHEN m.[Status] = N'Suppressed' THEN N'Suppressed'
                        WHEN EXISTS (SELECT 1 FROM [SentMessageRecipients] r
                            WHERE r.[SentMessageId] = m.[Id] AND r.[Outcome] = N'Complained') THEN N'Complained'
                        WHEN EXISTS (SELECT 1 FROM [SentMessageRecipients] r
                            WHERE r.[SentMessageId] = m.[Id]
                              AND r.[Outcome] IN (N'HardBounced', N'Rejected', N'SoftBounced')) THEN N'Bounced'
                        WHEN EXISTS (SELECT 1 FROM [SentMessageRecipients] r
                            WHERE r.[SentMessageId] = m.[Id] AND r.[Outcome] = N'Deferred') THEN N'Deferred'
                        WHEN EXISTS (SELECT 1 FROM [SentMessageRecipients] r
                            WHERE r.[SentMessageId] = m.[Id] AND r.[Outcome] = N'Accepted') THEN N'Sent'
                        WHEN EXISTS (SELECT 1 FROM [SentMessageRecipients] r
                            WHERE r.[SentMessageId] = m.[Id] AND r.[Outcome] = N'Delivered') THEN N'Delivered'
                        ELSE N'Sent'
                    END
                FROM [SentMessages] m;
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_SentMessages_TenantId_Activity_QueuedAtUtc",
                table: "SentMessages",
                columns: new[] { "TenantId", "Activity", "QueuedAtUtc" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_SentMessages_TenantId_Activity_QueuedAtUtc", table: "SentMessages");

            migrationBuilder.DropColumn(name: "Activity", table: "SentMessages");
        }
    }
}
