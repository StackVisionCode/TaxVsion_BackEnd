using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveReservedPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000012")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000013")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000014")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000015")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000016")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000018")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000036")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000037")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000044")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000081")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000085")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000086")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000100")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000101")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000112")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000125")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000128")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000129")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000130")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000132")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000133")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000134")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000135")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000136")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000137")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000138")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000139")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178")
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { true, false }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000038"),
                columns: new[] { "IsAssignableByTenant", "IsReserved" },
                values: new object[] { false, true }
            );

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
                    "IsReserved",
                    "MinPlanTier",
                    "Module",
                    "PlatformOnly",
                },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000012"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "signatures.request",
                        "Request signatures",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "signatures",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000013"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "documents.view",
                        "View documents",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "documents",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000014"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "documents.manage",
                        "Manage documents",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "documents",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000015"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "email.use",
                        "Use the email module",
                        false,
                        false,
                        false,
                        true,
                        1,
                        "email",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000016"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "comms.calls",
                        "Make calls and hold meetings",
                        false,
                        false,
                        false,
                        true,
                        1,
                        "comms",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000018"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "reports.view",
                        "View the dashboard and reports",
                        false,
                        false,
                        false,
                        true,
                        1,
                        "reports",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000020"),
                        "CustomerPortal",
                        "portal.miles.use",
                        "The client can use mileage tracking",
                        false,
                        true,
                        false,
                        true,
                        0,
                        "portal",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000036"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "signature.document.view",
                        "View signed documents and their details",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "signature",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000037"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "signature.document.download",
                        "Download the sealed file, the original or the certificate",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "signature",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000044"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "signature.certificate.verify",
                        "Verify signature certificates (public endpoint)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "signature",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000081"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "scribe.layouts.read",
                        "View email layouts (system and this office's)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "scribe",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000085"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "scribe.campaigns.read",
                        "View email campaigns built on templates (reserved, no endpoint yet)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "scribe",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000086"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "scribe.campaigns.write",
                        "Manage email campaigns built on templates (reserved, no endpoint yet)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "scribe",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000100"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "notification.campaign.view",
                        "View the office's email campaigns (reserved, no endpoint yet)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000101"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "notification.campaign.manage",
                        "Manage the office's email campaigns (reserved, no endpoint yet)",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "notification",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000112"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "payment_client.payment.refund",
                        "Refund a payment collected from one of the office's clients",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "payment_client",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000125"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "codes.code.issue",
                        "Issue benefit codes",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000128"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "codes.audit.read",
                        "View the code audit log",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000129"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "codes.redemption.read",
                        "View code redemptions",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000130"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "codes.compensation.manage",
                        "Manage promotional compensation",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "codes",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000132"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.program.read",
                        "View referral programs",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000133"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.program.manage",
                        "Manage referral programs",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000134"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.attribution.read",
                        "View referral attributions",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000135"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.fraud.read",
                        "View anti-fraud reviews",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000136"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.fraud.manage",
                        "Manage anti-fraud reviews",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000137"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.reward.read",
                        "View rewards",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000138"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.reward.manage",
                        "Manage non-monetary rewards",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000139"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "referrals.audit.read",
                        "View the referral audit log",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "referrals",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000178"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.availability.manage",
                        "Define working hours and calendar blocks",
                        false,
                        false,
                        false,
                        true,
                        0,
                        "calendar",
                        false,
                    },
                }
            );
        }
    }
}
