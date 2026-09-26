using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Auth.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Aditiva: una columna nullable y un backfill que solo marca roles que ya eran de portal. No
    /// borra ni cambia permisos de nadie.
    /// </summary>
    public partial class AddRoleTargetActorType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TargetActorType",
                table: "Roles",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true
            );

            // Backfill conservador: un rol CUSTOM cuyos permisos son TODOS de portal (y tiene al
            // menos uno) ya era un rol de clientes, solo que nadie lo había declarado. Marcarlo es
            // lo que lo vuelve editable (G8): hasta ahora, al reguardarlo se validaba contra el
            // staff y sus propios permisos se rechazaban.
            //
            // Los demás quedan en NULL a propósito — un rol mixto o vacío no se adivina, y NULL
            // conserva exactamente el comportamiento anterior.
            migrationBuilder.Sql(
                """
                UPDATE r
                SET TargetActorType = 'CustomerPortal'
                FROM Roles AS r
                WHERE r.IsSystem = 0
                  AND r.TargetActorType IS NULL
                  AND EXISTS (SELECT 1 FROM RolePermissions AS rp WHERE rp.RoleId = r.Id)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM RolePermissions AS rp
                      INNER JOIN Permissions AS p ON p.Id = rp.PermissionId
                      WHERE rp.RoleId = r.Id AND p.IsCustomerPortal = 0
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "TargetActorType", table: "Roles");
        }
    }
}
