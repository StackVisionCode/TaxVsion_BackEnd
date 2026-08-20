using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCalendarPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000174"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.read",
                        "Ver el calendario del tenant y consultar disponibilidad",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000175"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.write",
                        "Crear, mover y cancelar las citas propias",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000176"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.manage_all",
                        "Reorganizar agendas ajenas actuando como organizador (supervision)",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000177"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.types.manage",
                        "Definir los tipos de cita de la firma",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000178"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "calendar.availability.manage",
                        "Definir horarios de atencion y bloqueos de agenda",
                        true,
                        false,
                        false,
                        0,
                        "calendar",
                        false,
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000174")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000175")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000176")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000177")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000178")
            );
        }
    }
}
