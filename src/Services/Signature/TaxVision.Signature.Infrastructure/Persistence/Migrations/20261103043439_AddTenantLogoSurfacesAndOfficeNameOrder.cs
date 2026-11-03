using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantLogoSurfacesAndOfficeNameOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantLogoRefs",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Surface = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    LogoFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LogoContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LogoSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLogoRefs", x => new { x.TenantId, x.Surface });
                }
            );

            // El logo que ya estaba proyectado era el único que se publicaba: el del CRM.
            migrationBuilder.Sql(
                """
                INSERT INTO [TenantLogoRefs] ([TenantId], [Surface], [LogoFileId], [LogoContentType], [LogoSizeBytes], [UpdatedAtUtc])
                SELECT [TenantId], N'Crm', [LogoFileId], [LogoContentType], [LogoSizeBytes], [UpdatedAtUtc]
                FROM [TenantBrandingRefs]
                WHERE [LogoFileId] IS NOT NULL;
                """
            );

            // Expandir/contraer: las columnas viejas de logo (LogoFileId, LogoContentType, LogoSizeBytes) quedan
            // en la tabla sin mapear para que una instancia vieja siga leyendo mientras se despliega. Se borran
            // en una migración posterior, a mano (el modelo ya no las tiene, así que EF no las propone).
            migrationBuilder.AddColumn<DateTime>(
                name: "OfficeNameAsOfUtc",
                table: "TenantBrandingRefs",
                type: "datetime2",
                nullable: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OfficeNameAsOfUtc", table: "TenantBrandingRefs");

            // Vuelve el logo del CRM a las columnas viejas (siguen en la tabla) antes de borrar la nueva.
            migrationBuilder.Sql(
                """
                UPDATE b SET b.[LogoFileId] = l.[LogoFileId], b.[LogoContentType] = l.[LogoContentType], b.[LogoSizeBytes] = l.[LogoSizeBytes]
                FROM [TenantBrandingRefs] b INNER JOIN [TenantLogoRefs] l ON l.[TenantId] = b.[TenantId] AND l.[Surface] = N'Crm';
                """
            );

            migrationBuilder.DropTable(name: "TenantLogoRefs");
        }
    }
}
