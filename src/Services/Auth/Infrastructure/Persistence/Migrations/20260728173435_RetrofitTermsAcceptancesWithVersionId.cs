using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetrofitTermsAcceptancesWithVersionId : Migration
    {
        // Id fijo y deterministico de la fila semilla legacy en TermsVersions — referenciado tanto
        // por el INSERT de abajo como por el defaultValue de TermsVersionId, que es lo que hace de
        // "backfill" para las filas de TenantTermsAcceptances ya existentes (SQL Server aplica el
        // DEFAULT de una columna NOT NULL agregada a todas las filas preexistentes en el mismo ADD
        // COLUMN — no hace falta un UPDATE separado).
        private static readonly Guid LegacyTermsVersionId = new("6f7b8b9a-0000-4000-8000-000000000001");
        private static readonly DateTime MigrationDateUtc = new(2026, 7, 28, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // (1) Semilla legacy: representa "todo lo aceptado antes de que existiera el modelo
            // TermsVersion" — ContentUri/ContentHash quedan NULL a proposito, nunca hubo un
            // documento versionado y hasheado detras de esas aceptaciones historicas.
            migrationBuilder.InsertData(
                table: "TermsVersions",
                columns: new[]
                {
                    "Id",
                    "Kind",
                    "Version",
                    "ContentUri",
                    "ContentHash",
                    "EffectiveFromUtc",
                    "EffectiveUntilUtc",
                    "Locale",
                    "CreatedAtUtc",
                    "CreatedByUserId",
                },
                values: new object[]
                {
                    LegacyTermsVersionId,
                    "TermsOfService",
                    "legacy-2026-07-14",
                    null,
                    null,
                    MigrationDateUtc,
                    null,
                    "en-US",
                    MigrationDateUtc,
                    Guid.Empty,
                }
            );

            // (2)+(3) agregar columnas con su backfill vía DEFAULT: AcceptedInContext="LegacyPreV2"
            // y TermsVersionId=LegacyTermsVersionId para todas las filas preexistentes.
            // ContentHash no tiene backfill — las aceptaciones legacy nunca tuvieron un hash real.
            migrationBuilder.AddColumn<string>(
                name: "AcceptedInContext",
                table: "TenantTermsAcceptances",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "LegacyPreV2"
            );

            migrationBuilder.AddColumn<string>(
                name: "ContentHash",
                table: "TenantTermsAcceptances",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<Guid>(
                name: "TermsVersionId",
                table: "TenantTermsAcceptances",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: LegacyTermsVersionId
            );

            // (4) TermsVersionId/AcceptedInContext ya nacen NOT NULL arriba (todas las filas
            // preexistentes recibieron el DEFAULT) — ContentHash se queda nullable de forma
            // permanente, ver el doc-comment de TenantTermsAcceptance.cs.
            migrationBuilder.CreateIndex(
                name: "IX_TenantTermsAcceptances_TenantId_AcceptedByUserId_TermsVersionId",
                table: "TenantTermsAcceptances",
                columns: new[] { "TenantId", "AcceptedByUserId", "TermsVersionId" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TenantTermsAcceptances_TenantId_AcceptedByUserId_TermsVersionId",
                table: "TenantTermsAcceptances"
            );

            migrationBuilder.DropColumn(name: "AcceptedInContext", table: "TenantTermsAcceptances");

            migrationBuilder.DropColumn(name: "ContentHash", table: "TenantTermsAcceptances");

            migrationBuilder.DropColumn(name: "TermsVersionId", table: "TenantTermsAcceptances");

            migrationBuilder.DeleteData(table: "TermsVersions", keyColumn: "Id", keyValue: LegacyTermsVersionId);
        }
    }
}
