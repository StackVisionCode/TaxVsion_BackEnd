using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTasksPermissions : Migration
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
                        new Guid("a1000000-0000-0000-0000-000000000167"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "tasks.read",
                        "Ver las tareas del tenant",
                        true,
                        false,
                        false,
                        0,
                        "tasks",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000168"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "tasks.write",
                        "Crear, editar, cerrar y reabrir tareas propias o asignadas a uno mismo",
                        true,
                        false,
                        false,
                        0,
                        "tasks",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000169"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "tasks.assign",
                        "Asignar una tarea a otra persona del tenant (sin restricción de dirección)",
                        true,
                        false,
                        false,
                        0,
                        "tasks",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000170"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "tasks.manage_all",
                        "Cerrar, editar o reasignar la tarea de cualquier usuario del tenant (supervisión)",
                        true,
                        false,
                        false,
                        0,
                        "tasks",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000171"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "tasks.templates.manage",
                        "Crear y editar las plantillas de tarea de la firma",
                        true,
                        false,
                        false,
                        0,
                        "tasks",
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
                keyValue: new Guid("a1000000-0000-0000-0000-000000000167")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000168")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000169")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000170")
            );

            migrationBuilder.DeleteData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000171")
            );
        }
    }
}
