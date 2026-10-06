using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Postmaster.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Renombra, no recrea. EF generó DropTable+CreateTable porque cambió el nombre de la entidad, y
    /// eso habría borrado los buzones conectados: las oficinas dejarían de enviar desde su dirección
    /// y pasarían al remitente del sistema sin que nadie lo notara.
    /// </summary>
    public partial class RenameTenantOAuthAccountsToConnectedMailboxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "TenantOAuthAccounts", newName: "ConnectedMailboxes");

            // SQL Server tampoco renombra la PK, y EF la espera como PK_<tabla>.
            migrationBuilder.Sql("EXEC sp_rename N'PK_TenantOAuthAccounts', N'PK_ConnectedMailboxes', N'OBJECT';");

            // SQL Server no renombra los índices con la tabla.
            migrationBuilder.RenameIndex(
                name: "IX_TenantOAuthAccounts_TenantId_AccountId",
                table: "ConnectedMailboxes",
                newName: "IX_ConnectedMailboxes_TenantId_AccountId"
            );
            migrationBuilder.RenameIndex(
                name: "IX_TenantOAuthAccounts_TenantId_IsActive_ConnectedAtUtc",
                table: "ConnectedMailboxes",
                newName: "IX_ConnectedMailboxes_TenantId_IsActive_ConnectedAtUtc"
            );

            // ProviderScope se guarda como texto: sin esto, leer un envío viejo revienta al parsear.
            // Reetiquetar es exacto — esos correos salieron por este mismo carril.
            migrationBuilder.Sql(
                "UPDATE [SentMessages] SET [RequiredProviderScope] = N'TenantMailbox' "
                    + "WHERE [RequiredProviderScope] = N'TenantOAuth';"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE [SentMessages] SET [RequiredProviderScope] = N'TenantOAuth' "
                    + "WHERE [RequiredProviderScope] = N'TenantMailbox';"
            );

            // La tabla primero: los RenameIndex de abajo la nombran por su nombre ya restaurado.
            migrationBuilder.RenameTable(name: "ConnectedMailboxes", newName: "TenantOAuthAccounts");

            migrationBuilder.Sql("EXEC sp_rename N'PK_ConnectedMailboxes', N'PK_TenantOAuthAccounts', N'OBJECT';");

            migrationBuilder.RenameIndex(
                name: "IX_ConnectedMailboxes_TenantId_IsActive_ConnectedAtUtc",
                table: "TenantOAuthAccounts",
                newName: "IX_TenantOAuthAccounts_TenantId_IsActive_ConnectedAtUtc"
            );
            migrationBuilder.RenameIndex(
                name: "IX_ConnectedMailboxes_TenantId_AccountId",
                table: "TenantOAuthAccounts",
                newName: "IX_TenantOAuthAccounts_TenantId_AccountId"
            );
        }
    }
}
