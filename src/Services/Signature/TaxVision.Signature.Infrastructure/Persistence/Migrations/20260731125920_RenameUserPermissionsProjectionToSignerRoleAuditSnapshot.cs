using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Signature.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RenameUserPermissionsProjectionToSignerRoleAuditSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename only — el propósito de esta migración es renombrar la vieja
            // proyección de auditoría (que colisionaba de nombre con la de RBAC Fase 7)
            // sin perder los datos ya existentes. Ver AuthzUserPermissionsProjection.cs
            // para el contexto de la colisión original.
            migrationBuilder.RenameTable(name: "UserPermissionsProjections", newName: "SignerRoleAuditSnapshots");

            migrationBuilder.RenameIndex(
                table: "SignerRoleAuditSnapshots",
                name: "IX_UserPermissionsProjections_TenantId_UserId",
                newName: "IX_SignerRoleAuditSnapshots_TenantId_UserId"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameIndex(
                table: "SignerRoleAuditSnapshots",
                name: "IX_SignerRoleAuditSnapshots_TenantId_UserId",
                newName: "IX_UserPermissionsProjections_TenantId_UserId"
            );

            migrationBuilder.RenameTable(name: "SignerRoleAuditSnapshots", newName: "UserPermissionsProjections");
        }
    }
}
