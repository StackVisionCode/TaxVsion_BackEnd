using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunicationPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Permissions",
                columns: new[] { "Id", "Code", "Description", "IsCustomerPortal", "Module" },
                values: new object[,]
                {
                    {
                        new Guid("a1000000-0000-0000-0000-000000000045"),
                        "communication.chat.start",
                        "Iniciar conversaciones de chat",
                        true,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000046"),
                        "communication.chat.reply",
                        "Responder en conversaciones de chat",
                        true,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000047"),
                        "communication.chat.moderate",
                        "Moderar mensajes en conversaciones del tenant",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000048"),
                        "communication.support.open",
                        "Abrir chat de soporte hacia el PlatformTenant",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000049"),
                        "communication.support.agent",
                        "Atender chats de soporte como agente (PlatformTenant)",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000050"),
                        "communication.call.start",
                        "Iniciar llamadas de audio 1:1",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000051"),
                        "communication.videocall.start",
                        "Iniciar llamadas de video 1:1",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000052"),
                        "communication.call.record",
                        "Grabar llamadas 1:1 (con banner de disclosure)",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000053"),
                        "communication.meeting.create",
                        "Crear reuniones multi-party",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000054"),
                        "communication.meeting.join",
                        "Unirse a reuniones (previa invitación válida)",
                        true,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000055"),
                        "communication.meeting.host",
                        "Actuar como host de reuniones (waiting room, mute all, transfer)",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000056"),
                        "communication.meeting.record",
                        "Grabar reuniones (con banner de disclosure)",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000057"),
                        "communication.screenshot.create",
                        "Adjuntar screenshots/voice/video en chat",
                        true,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000058"),
                        "communication.group.create",
                        "Crear grupos internos por tenant",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000059"),
                        "communication.group.manage_members",
                        "Gestionar miembros de grupos internos",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000060"),
                        "communication.notification.read",
                        "Consultar notificaciones in-app propias",
                        true,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000061"),
                        "communication.settings.manage",
                        "Gestionar la configuración de Communication del tenant",
                        false,
                        "communication",
                    },
                    {
                        new Guid("a1000000-0000-0000-0000-000000000062"),
                        "communication.analytics.read",
                        "Consultar analytics de Communication del tenant",
                        false,
                        "communication",
                    },
                }
            );

            // Roles de sistema: defaults por rol sin tocar roles custom del tenant.
            // NOT EXISTS protege el rerun. Customer Portal recibe permisos limitados
            // (chat, unirse a meeting, screenshots, notifs). Nunca host, record ni admin.
            migrationBuilder.Sql(
                """
                INSERT INTO RolePermissions (RoleId, PermissionId)
                SELECT r.Id, p.Id
                FROM Roles AS r
                CROSS JOIN Permissions AS p
                WHERE r.IsSystem = 1
                  AND (
                    (r.Name = N'Tenant Admin' AND p.Code IN (
                      N'communication.chat.start',
                      N'communication.chat.reply',
                      N'communication.chat.moderate',
                      N'communication.support.open',
                      N'communication.call.start',
                      N'communication.videocall.start',
                      N'communication.call.record',
                      N'communication.meeting.create',
                      N'communication.meeting.join',
                      N'communication.meeting.host',
                      N'communication.meeting.record',
                      N'communication.screenshot.create',
                      N'communication.group.create',
                      N'communication.group.manage_members',
                      N'communication.notification.read',
                      N'communication.settings.manage',
                      N'communication.analytics.read'))
                    OR
                    (r.Name = N'Employee' AND p.Code IN (
                      N'communication.chat.start',
                      N'communication.chat.reply',
                      N'communication.support.open',
                      N'communication.call.start',
                      N'communication.videocall.start',
                      N'communication.meeting.create',
                      N'communication.meeting.join',
                      N'communication.meeting.host',
                      N'communication.screenshot.create',
                      N'communication.notification.read'))
                    OR
                    (r.Name = N'Customer Portal' AND p.Code IN (
                      N'communication.chat.start',
                      N'communication.chat.reply',
                      N'communication.support.open',
                      N'communication.meeting.join',
                      N'communication.screenshot.create',
                      N'communication.notification.read'))
                  )
                  AND NOT EXISTS (
                    SELECT 1
                    FROM RolePermissions AS rp
                    WHERE rp.RoleId = r.Id AND rp.PermissionId = p.Id
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            for (var i = 45; i <= 62; i++)
            {
                migrationBuilder.DeleteData(
                    table: "Permissions",
                    keyColumn: "Id",
                    keyValue: new Guid($"a1000000-0000-0000-0000-{i:D12}")
                );
            }
        }
    }
}
