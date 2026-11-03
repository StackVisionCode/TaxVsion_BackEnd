using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.PaymentClient.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantPaymentConfigWebhookSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProviderWebhookId",
                table: "TenantPaymentConfigs",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WebhookSetup",
                table: "TenantPaymentConfigs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            // Los secretos ya guardados los pegó la oficina a mano. sp_executesql: la columna es de esta
            // misma migración y el lote no la conoce al compilarse.
            migrationBuilder.Sql(
                "EXEC sp_executesql N'UPDATE [TenantPaymentConfigs] SET [WebhookSetup] = N''Manual'' WHERE [WebhookSecretEncrypted] IS NOT NULL';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProviderWebhookId",
                table: "TenantPaymentConfigs");

            migrationBuilder.DropColumn(
                name: "WebhookSetup",
                table: "TenantPaymentConfigs");
        }
    }
}
