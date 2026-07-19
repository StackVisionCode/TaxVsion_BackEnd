using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Bug real de produccion (2026-07-19): con EnforceHostResolution=true,
    /// TenantHostResolutionMiddleware exige que TODO Host resuelva a un TenantDomain
    /// activo (incluido /auth/login, no esta en la lista de rutas exentas). El tenant
    /// Platform (8f58a521-..., sembrado en AddInvitationActorsAndPlatformTenant) nunca
    /// recibio su propio TenantDomain: TenantCreatedConsumer y TenantDomainBackfillService
    /// saltan a proposito los tenants Kind=Platform (esa logica es solo para oficinas), y
    /// ademas "platform-internal" (el SubDomain literal del tenant) esta en la lista de
    /// slugs reservados de SubdomainSlug — asi que ni siquiera hubiera podido crearse via
    /// el flujo normal de subdominio. Resultado: ningun PlatformAdmin podia loguearse en
    /// ningun ambiente con host-enforcement activo, sin importar que Host se probara.
    /// Esta migracion siembra el TenantDomain que le falta al tenant Platform, apuntando
    /// al host real de la API (TAXVISION_DOMAIN en Caddyfile/docker-compose, default
    /// api.taxprocore.com) — el mismo patron de InsertData ya usado para sembrar el propio
    /// tenant Platform.
    /// </summary>
    public partial class SeedPlatformTenantDomain : Migration
    {
        private static readonly Guid PlatformTenantDomainId = new("f1b2c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
        private static readonly Guid PlatformTenantId = new("8f58a521-4c25-4d91-9f4e-7ad5df14c001");

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "TenantDomains",
                columns: new[]
                {
                    "Id",
                    "TenantId",
                    "DomainType",
                    "Host",
                    "SubdomainSlug",
                    "Status",
                    "IsPrimary",
                    "CloudflareCustomHostnameId",
                    "VerificationMethod",
                    "VerifiedAtUtc",
                    "CreatedByUserId",
                    "CreatedAtUtc",
                },
                values: new object[]
                {
                    PlatformTenantDomainId,
                    PlatformTenantId,
                    "CustomHostname",
                    "api.taxprocore.com",
                    null,
                    "Active",
                    true,
                    null,
                    null,
                    null,
                    Guid.Empty,
                    new DateTime(2026, 7, 19, 0, 0, 0, 0, DateTimeKind.Utc),
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "TenantDomains", keyColumn: "Id", keyValue: PlatformTenantDomainId);
        }
    }
}
