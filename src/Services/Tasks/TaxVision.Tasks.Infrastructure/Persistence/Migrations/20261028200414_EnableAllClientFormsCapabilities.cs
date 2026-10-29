using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Tasks.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Todo encendido por defecto (decisión del dueño): las oficinas con fila guardada conservaban
    /// sub-flags en false de antes del cambio de defaults. Intake sigue exigiendo Turnstile en runtime.
    /// </summary>
    public partial class EnableAllClientFormsCapabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE [FormModuleSettings]
                SET [ExternalParticipantsEnabled] = 1,
                    [IntakeLinksEnabled] = 1,
                    [AutoCustomerCreationEnabled] = 1,
                    [SignatureStepEnabled] = 1,
                    [PushNotificationsEnabled] = 1;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sin vuelta atrás: los valores previos por oficina no se guardaron.
        }
    }
}
