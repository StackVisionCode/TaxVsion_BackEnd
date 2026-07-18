using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentAppAndPaymentClientPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[]
                {
                    "Id",
                    "Code",
                    "Description",
                    "IsAssignableByTenant",
                    "IsCustomerPortal",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000103"),
                        "payment_app.saas_payment.read",
                        "Ver los pagos SaaS (suscripción/seats/add-ons) del propio tenant",
                        true,
                        false,
                        0,
                        "payment_app",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000104"),
                        "payment_app.saas_payment.refund",
                        "Reembolsar un pago SaaS del propio tenant",
                        true,
                        false,
                        0,
                        "payment_app",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000105"),
                        "payment_app.provider_customer.read",
                        "Ver el método de pago guardado (provider customer) del propio tenant",
                        true,
                        false,
                        0,
                        "payment_app",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000106"),
                        "payment_app.provider_customer.manage",
                        "Gestionar el método de pago guardado del propio tenant",
                        true,
                        false,
                        0,
                        "payment_app",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000107"),
                        "payment_app.admin.cross_tenant",
                        "Ver pagos SaaS de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)",
                        false,
                        false,
                        0,
                        "payment_app",
                        true,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000108"),
                        "payment_client.config.read",
                        "Ver la configuración de cobro (Stripe DirectApiKeys/Connect) del propio tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000109"),
                        "payment_client.config.manage",
                        "Configurar el modo/credenciales de cobro del propio tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000110"),
                        "payment_client.payment.read",
                        "Ver los pagos que el tenant cobró a sus propios clientes",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000111"),
                        "payment_client.payment.charge",
                        "Cobrar un pago a un cliente del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000112"),
                        "payment_client.payment.refund",
                        "Reembolsar un pago cobrado a un cliente del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000113"),
                        "payment_client.payment_link.read",
                        "Ver los links de pago del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000114"),
                        "payment_client.payment_link.manage",
                        "Crear y gestionar links de pago del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000115"),
                        "payment_client.connect_account.read",
                        "Ver el estado de la cuenta Stripe Connect del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000116"),
                        "payment_client.connect_account.onboard",
                        "Iniciar el onboarding de la cuenta Stripe Connect del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000117"),
                        "payment_client.payout.read",
                        "Ver los payouts programados del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000118"),
                        "payment_client.payout.manage",
                        "Gestionar el calendario de payouts del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000119"),
                        "payment_client.recurring.read",
                        "Ver los pagos recurrentes configurados del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000120"),
                        "payment_client.recurring.manage",
                        "Crear y gestionar pagos recurrentes del tenant",
                        true,
                        false,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000121"),
                        "payment_client.admin.cross_tenant",
                        "Ver pagos de CUALQUIER tenant, incluso suspendido (soporte/investigación, uso exclusivo de plataforma)",
                        false,
                        false,
                        0,
                        "payment_client",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000103")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000104")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000105")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000106")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000107")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000108")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000109")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000110")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000111")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000113")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000114")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000115")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000116")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000117")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000118")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000119")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000120")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000121")
            );
        }
    }
}
