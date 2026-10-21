using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientFormsPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "AllowedActorTypes", "Code", "Description", "GatedModule", "IsAssignableByTenant", "IsCustomerPortal", "IsDangerous", "IsReserved", "MinPlanTier", "Module", "PlatformOnly" },
                values: new object[,]
                {
                    { new Guid("a1000000-0000-0000-0000-000000000196"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_templates.read", "View client form templates, versions and the template library.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000197"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_templates.manage", "Create and edit client form templates.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000198"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_templates.publish", "Publish new versions of client form templates.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000199"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.read", "View client form requests and the answers of assigned clients.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000200"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.manage", "Assign, cancel and remind client forms.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000201"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.review", "Review submitted client forms, request changes and complete them.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000202"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.export", "Run reports and export client form answers.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000203"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.apply_profile", "Apply approved client form answers to the client profile.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000204"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_submissions.reveal", "Reveal sensitive tax identifiers submitted in client forms.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000205"), "PlatformAdmin", "tasks.forms.library.manage", "Manage the TaxVision client form library.", "planner", false, false, false, false, 0, "tasks", true },
                    { new Guid("a1000000-0000-0000-0000-000000000206"), "PlatformAdmin", "tasks.forms.platform.manage", "Enable client forms for tenants.", "planner", false, false, false, false, 0, "tasks", true },
                    { new Guid("a1000000-0000-0000-0000-000000000207"), "CustomerPortal", "tasks.portal.forms", "Complete forms requested by the tax office in the client portal.", "planner", true, true, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000208"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_templates.configure_actions", "Configure data mapping and post-submission actions for client form templates.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000209"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_requests.send_external", "Send client forms to people who are not registered clients yet.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000210"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_intake_links.manage", "Manage public intake links for client forms.", "planner", true, false, false, false, 0, "tasks", false },
                    { new Guid("a1000000-0000-0000-0000-000000000211"), "TenantEmployee,TenantAdmin,PlatformAdmin", "tasks.form_consents.manage", "Manage consent notices used in client forms.", "planner", true, false, false, false, 0, "tasks", false }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000196"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000197"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000198"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000199"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000200"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000201"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000202"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000203"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000204"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000205"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000206"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000207"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000208"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000209"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000210"));

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000211"));
        }
    }
}
