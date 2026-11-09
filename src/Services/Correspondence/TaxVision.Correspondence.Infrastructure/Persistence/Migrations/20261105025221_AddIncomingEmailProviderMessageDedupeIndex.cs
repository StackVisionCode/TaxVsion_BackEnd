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
            // Tabla de alto volumen: ventana de mantenimiento.
            // Q-COR-7: no se borra ningún correo; el duplicado recibe un InternetMessageId propio y sale del índice filtrado.
            // ONLINE solo donde la edición lo permite (Enterprise/Developer, Azure SQL); dinámico porque se valida al compilar.
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT [Id], [InternetMessageId], ROW_NUMBER() OVER (
                        PARTITION BY [TenantId], [AccountId], [ProviderMessageId] ORDER BY [ReceivedAtUtc], [Id]) AS rn
                    FROM [IncomingEmails] WHERE [InternetMessageId] IS NULL
                )
                UPDATE ranked SET [InternetMessageId] = CONCAT(N'<duplicate-', LOWER(CONVERT(nvarchar(36), [Id])), N'@taxvision.invalid>')
                WHERE rn > 1;

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
                table: "IncomingEmails"
            );
        }
    }
}
