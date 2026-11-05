using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Correspondence.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncomingEmailProviderMessageDedupeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tabla de alto volumen: ventana de mantenimiento. Antes, Q-COR-7 vacía (no se borran duplicados acá).
            // ONLINE solo donde la edición lo permite (Enterprise/Developer, Azure SQL); dinámico porque se valida al compilar.
            migrationBuilder.Sql(
                """
                IF EXISTS (
                    SELECT 1 FROM [IncomingEmails] WHERE [InternetMessageId] IS NULL
                    GROUP BY [TenantId], [AccountId], [ProviderMessageId] HAVING COUNT(*) > 1)
                    THROW 50001, 'IncomingEmails has duplicates without InternetMessageId (Q-COR-7). Resolve them before creating IX_IncomingEmails_TenantId_AccountId_ProviderMessageId_NoInternetId.', 1;

                DECLARE @sql nvarchar(max) = N'CREATE UNIQUE INDEX [IX_IncomingEmails_TenantId_AccountId_ProviderMessageId_NoInternetId]
                    ON [IncomingEmails] ([TenantId], [AccountId], [ProviderMessageId])
                    WHERE [InternetMessageId] IS NULL';
                IF CAST(SERVERPROPERTY('EngineEdition') AS int) IN (3, 5, 8)
                    SET @sql += N' WITH (ONLINE = ON)';
                EXEC sp_executesql @sql;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_IncomingEmails_TenantId_AccountId_ProviderMessageId_NoInternetId",
                table: "IncomingEmails");
        }
    }
}
