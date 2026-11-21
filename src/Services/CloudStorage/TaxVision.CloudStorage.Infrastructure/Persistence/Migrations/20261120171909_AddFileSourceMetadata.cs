using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFileSourceMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RelatedCustomerId",
                table: "Files",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTime>(
                name: "SourceDeclaredAtUtc",
                table: "Files",
                type: "datetime2",
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "SourceResourceId",
                table: "Files",
                type: "uniqueidentifier",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SourceResourceType",
                table: "Files",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SourceRole",
                table: "Files",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "SourceService",
                table: "Files",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            // Relleno de columnas nuevas (aprobado por el dueño el 2026-11-20). EXEC: la columna no existe
            // todavía cuando SQL Server compila el lote.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE [Files] SET [RelatedCustomerId] = [OwnerId] "
                    + "WHERE [OwnerType] = N''Customer'' AND [RelatedCustomerId] IS NULL')"
            );

            // Servicio que guardó cada archivo, desde el log de guardados (service=x). Solo informativo.
            migrationBuilder.Sql(
                "EXEC(N'UPDATE f SET f.[SourceService] = SUBSTRING(l.[Details], 9, 64) "
                    + "FROM [Files] f JOIN [StorageAccessLogs] l ON l.[TenantId] = f.[TenantId] AND l.[FileId] = f.[Id] "
                    + "WHERE l.[Action] = N''upload.from-source'' AND l.[Outcome] <> N''rejected'' "
                    + "AND l.[Details] LIKE N''service=%'' AND l.[Details] NOT LIKE N''%;%'' "
                    + "AND f.[SourceService] IS NULL')"
            );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Library_Customer",
                    table: "Files",
                    columns: new[] { "TenantId", "RelatedCustomerId", "CreatedAtUtc", "Id" },
                    descending: new[] { false, false, true, true },
                    filter: "[Status] = N'Available' AND [RelatedCustomerId] IS NOT NULL"
                )
                .Annotation(
                    "SqlServer:Include",
                    new[]
                    {
                        "OriginalName",
                        "OwnerType",
                        "FolderType",
                        "SourceResourceType",
                        "SourceRole",
                        "CreatedBy",
                        "TaxYear",
                        "SizeBytes",
                        "DetectedContentType",
                        "DeclaredContentType",
                    }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Library_Name",
                    table: "Files",
                    columns: new[] { "TenantId", "OriginalName", "Id" },
                    filter: "[Status] = N'Available'"
                )
                .Annotation(
                    "SqlServer:Include",
                    new[]
                    {
                        "OwnerType",
                        "OwnerId",
                        "FolderType",
                        "SourceResourceType",
                        "SourceRole",
                        "RelatedCustomerId",
                        "CreatedBy",
                        "TaxYear",
                        "SizeBytes",
                        "CreatedAtUtc",
                        "DetectedContentType",
                        "DeclaredContentType",
                    }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Library_Recent",
                    table: "Files",
                    columns: new[] { "TenantId", "CreatedAtUtc", "Id" },
                    descending: new[] { false, true, true },
                    filter: "[Status] = N'Available'"
                )
                .Annotation(
                    "SqlServer:Include",
                    new[]
                    {
                        "OriginalName",
                        "OwnerType",
                        "OwnerId",
                        "FolderType",
                        "SourceResourceType",
                        "SourceRole",
                        "RelatedCustomerId",
                        "CreatedBy",
                        "TaxYear",
                        "SizeBytes",
                        "DetectedContentType",
                        "DeclaredContentType",
                    }
                );

            migrationBuilder
                .CreateIndex(
                    name: "IX_Files_Source_Resource",
                    table: "Files",
                    columns: new[] { "TenantId", "SourceResourceType", "SourceResourceId" },
                    filter: "[SourceResourceId] IS NOT NULL"
                )
                .Annotation("SqlServer:Include", new[] { "SourceRole", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Files_Library_Customer", table: "Files");

            migrationBuilder.DropIndex(name: "IX_Files_Library_Name", table: "Files");

            migrationBuilder.DropIndex(name: "IX_Files_Library_Recent", table: "Files");

            migrationBuilder.DropIndex(name: "IX_Files_Source_Resource", table: "Files");

            migrationBuilder.DropColumn(name: "RelatedCustomerId", table: "Files");

            migrationBuilder.DropColumn(name: "SourceDeclaredAtUtc", table: "Files");

            migrationBuilder.DropColumn(name: "SourceResourceId", table: "Files");

            migrationBuilder.DropColumn(name: "SourceResourceType", table: "Files");

            migrationBuilder.DropColumn(name: "SourceRole", table: "Files");

            migrationBuilder.DropColumn(name: "SourceService", table: "Files");
        }
    }
}
