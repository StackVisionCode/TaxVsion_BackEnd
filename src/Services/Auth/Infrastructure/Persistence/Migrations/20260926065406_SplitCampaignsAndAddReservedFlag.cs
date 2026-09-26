using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Escrita a mano a partir de la que generó dotnet-ef: el scaffolding emitía un UpdateData por
    /// cada una de las ~190 filas del catálogo para poner IsReserved en false, que es justo lo que
    /// ya hace el defaultValue de la columna. Acá queda solo lo que de verdad cambia.
    /// </summary>
    public partial class SplitCampaignsAndAddReservedFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Declarado pero sin ningún endpoint que lo exija. El defaultValue cubre las filas
            // existentes: ninguna es reservada salvo la que se marca más abajo.
            migrationBuilder.AddColumn<bool>(
                name: "IsReserved",
                table: "Permissions",
                type: "bit",
                nullable: false,
                defaultValue: false
            );

            // campaigns.manage deja de cubrir ver y enviar.
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                column: "Description",
                value: "Crear y editar campañas, contactos y listas"
            );

            // invoicing.manage deja de cubrir el emisor legal.
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000181"),
                column: "Description",
                value: "Crear, emitir y gestionar facturas de clientes"
            );

            // portal.miles.use: no existe el módulo ni ningún endpoint que lo exija. Queda reservado
            // y deja de ofrecerse en el cajón de accesos hasta que la función exista.
            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
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
                        new Guid("a1000000-0000-0000-0000-000000000183"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "campaigns.view",
                        "Ver campañas, contactos, listas, programaciones y corridas",
                        true,
                        false,
                        false,
                        false,
                        1,
                        "campaigns",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000184"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "campaigns.send",
                        "Disparar o programar el envío de una campaña",
                        true,
                        false,
                        false,
                        false,
                        1,
                        "campaigns",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000185"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "campaigns.senders.manage",
                        "Administrar los perfiles de remitente de las campañas",
                        true,
                        false,
                        false,
                        false,
                        1,
                        "campaigns",
                        false,
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000186"),
                        "TenantEmployee,TenantAdmin,PlatformAdmin",
                        "invoicing.issuer.manage",
                        "Editar el emisor legal de las facturas del tenant",
                        true,
                        false,
                        false,
                        false,
                        0,
                        "billing",
                        false,
                    },
                }
            );

            // No bloquear de más: un rol CUSTOM que hoy tiene campaigns.manage perdería ver y enviar,
            // porque esas acciones pasan a exigir los permisos nuevos. Se le conceden acá los tres del
            // servicio (ver, enviar y, como ya administraba todo, los remitentes). Los roles de SISTEMA
            // no se tocan por migración: los resincroniza SystemRolePermissionsSyncService al arrancar
            // Auth, que además publica los eventos que las proyecciones necesitan.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT rp.RoleId, nueva.PermissionId
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                CROSS APPLY (
                    VALUES
                        (CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000183')),
                        (CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000184')),
                        (CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000185'))
                ) AS nueva(PermissionId)
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000017'
                  AND r.IsSystem = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM RolePermissions AS ya
                      WHERE ya.RoleId = rp.RoleId AND ya.PermissionId = nueva.PermissionId
                  );
                """
            );

            // Mismo criterio para el emisor legal: un rol custom que tenía invoicing.manage seguía
            // pudiendo editarlo, así que conserva esa capacidad con el permiso nuevo.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT rp.RoleId, CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000186')
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000181'
                  AND r.IsSystem = 0
                  AND NOT EXISTS (
                      SELECT 1 FROM RolePermissions AS ya
                      WHERE ya.RoleId = rp.RoleId
                        AND ya.PermissionId = CONVERT(uniqueidentifier, 'a1000000-0000-0000-0000-000000000186')
                  );
                """
            );

            // Y portal.miles.use se retira de los roles custom que lo tuvieran: ya no es asignable.
            migrationBuilder.Sql(
                """
                DELETE rp
                FROM RolePermissions AS rp
                INNER JOIN Roles AS r ON r.Id = rp.RoleId
                WHERE rp.PermissionId = 'a1000000-0000-0000-0000-000000000020' AND r.IsSystem = 0;
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                DELETE FROM RolePermissions
                WHERE PermissionId IN (
                    'a1000000-0000-0000-0000-000000000183',
                    'a1000000-0000-0000-0000-000000000184',
                    'a1000000-0000-0000-0000-000000000185',
                    'a1000000-0000-0000-0000-000000000186'
                );
                """
            );

            foreach (
                var id in new[]
                {
                    "a1000000-0000-0000-0000-000000000183",
                    "a1000000-0000-0000-0000-000000000184",
                    "a1000000-0000-0000-0000-000000000185",
                    "a1000000-0000-0000-0000-000000000186",
                }
            )
            {
                migrationBuilder.DeleteData(table: "Permissions", keyColumn: "Id", keyValue: new Guid(id));
            }

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000020"),
                column: "IsAssignableByTenant",
                value: true
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000017"),
                column: "Description",
                value: "Gestionar campañas"
            );

            migrationBuilder.UpdateData(
                table: "Permissions",
                keyColumn: "Id",
                keyValue: new Guid("a1000000-0000-0000-0000-000000000181"),
                column: "Description",
                value: "Crear, emitir y gestionar facturas de clientes y los datos del emisor"
            );

            migrationBuilder.DropColumn(name: "IsReserved", table: "Permissions");
        }
    }
}
