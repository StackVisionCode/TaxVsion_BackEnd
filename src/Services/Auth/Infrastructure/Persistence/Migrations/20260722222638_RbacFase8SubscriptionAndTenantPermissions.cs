using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RbacFase8SubscriptionAndTenantPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "AllowedActorTypes",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "IsDangerous",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000143"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "subscription.plan.change",
                        "Cambiar plan, activar, cancelar y gestionar el ciclo de vida de la suscripción del propio tenant",
                        false,
                        false,
                        false,
                        0,
                        "subscription",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000144"),
                        "PlatformAdmin",
                        "subscription.suspend",
                        "Suspender la suscripción de cualquier tenant (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "subscription",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000145"),
                        "PlatformAdmin",
                        "subscription.reactivate",
                        "Reactivar la suscripción de cualquier tenant (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "subscription",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000146"),
                        "PlatformAdmin",
                        "subscription.renew",
                        "Renovación manual de la suscripción de cualquier tenant, mientras no exista Billing (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "subscription",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000147"),
                        "PlatformAdmin",
                        "subscription.admin.cross_tenant",
                        "Consultar renovaciones próximas, seats vencidos y suscripciones en mora de CUALQUIER tenant, y forzar el recálculo de entitlements (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "subscription",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000148"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "seats.manage",
                        "Comprar, asignar, liberar, reasignar y renovar seats del propio tenant",
                        false,
                        false,
                        false,
                        0,
                        "seats",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000149"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "addons.manage",
                        "Comprar, cancelar y renovar add-ons del propio tenant",
                        false,
                        false,
                        false,
                        0,
                        "addons",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000150"),
                        "PlatformAdmin",
                        "tenant.status.change",
                        "Cambiar el estado de cualquier tenant (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "tenant",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000151"),
                        "PlatformAdmin",
                        "tenant.list.view",
                        "Listar todos los tenants de la plataforma (uso exclusivo de plataforma)",
                        false,
                        false,
                        false,
                        0,
                        "tenant",
                        true,
                    },
                }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000143")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000144")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000145")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000146")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000147")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000148")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000149")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000150")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000151")
            );
        }
    }
}
