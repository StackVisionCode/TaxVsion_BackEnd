using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Notification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameNotificationRecipientPermissionsProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename only — estas dos tablas colisionaban de nombre con las proyecciones de
            // AUTORIZACIÓN de RBAC Fase 7 (AuthzUserPermissionsProjection/AuthzRolePermissionsProjection).
            // Preserva los datos existentes; ver AuthzUserPermissionsProjection.cs para el contexto.
            migrationBuilder.RenameTable(
                name: "UserPermissionsProjections",
                newName: "NotificationRecipientPermissionsProjections"
            );

            migrationBuilder.RenameTable(
                name: "RolePermissionsProjections",
                newName: "NotificationRecipientRolePermissionsProjections"
            );

            migrationBuilder.RenameIndex(
                table: "NotificationRecipientPermissionsProjections",
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                newName: "IX_NotificationRecipientPermissionsProjections_TenantId_UserId"
            );

            migrationBuilder.RenameIndex(
                table: "NotificationRecipientPermissionsProjections",
                name: "IX_UserPermissionsProjections_TenantId_IsActive",
                newName: "IX_NotificationRecipientPermissionsProjections_TenantId_IsActive"
            );

            migrationBuilder.RenameIndex(
                table: "NotificationRecipientRolePermissionsProjections",
                name: "IX_RolePermissionsProjections_TenantId",
                newName: "IX_NotificationRecipientRolePermissionsProjections_TenantId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                table: "NotificationRecipientPermissionsProjections",
                name: "IX_NotificationRecipientPermissionsProjections_TenantId_UserId",
                newName: "IX_UserPermissionsProjections_TenantId_UserId"
            );

            migrationBuilder.RenameIndex(
                table: "NotificationRecipientPermissionsProjections",
                name: "IX_NotificationRecipientPermissionsProjections_TenantId_IsActive",
                newName: "IX_UserPermissionsProjections_TenantId_IsActive"
            );

            migrationBuilder.RenameIndex(
                table: "NotificationRecipientRolePermissionsProjections",
                name: "IX_NotificationRecipientRolePermissionsProjections_TenantId",
                newName: "IX_RolePermissionsProjections_TenantId"
            );

            migrationBuilder.RenameTable(
                name: "NotificationRecipientPermissionsProjections",
                newName: "UserPermissionsProjections"
            );

            migrationBuilder.RenameTable(
                name: "NotificationRecipientRolePermissionsProjections",
                newName: "RolePermissionsProjections"
            );
        }
    }
}
