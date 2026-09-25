using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Backfill (punto 1f): crea una fila de asignación PRIMARY para los clientes existentes que aún no
    /// tienen ninguna, antes de encender la visibilidad por asignación. Regla acordada:
    /// preparador explícito → él; creado por un EMPLEADO elegible → él; creado por admin/desconocido →
    /// sin fila (queda admin-only; el admin lo ve por customers.view_all y lo reparte con bulk-assign).
    /// Set-based e idempotente (NOT EXISTS): re-ejecutarla no duplica.
    /// </summary>
    public partial class BackfillCustomerAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO CustomerAssignments (Id, TenantId, CustomerId, UserId, IsPrimary, AssignedByUserId, AssignedAtUtc)
                SELECT NEWID(), c.TenantId, c.Id,
                       COALESCE(c.AssignedPreparerUserId, c.CreatedByUserId),
                       1, c.CreatedByUserId, SYSUTCDATETIME()
                FROM Customers c
                WHERE NOT EXISTS (SELECT 1 FROM CustomerAssignments a WHERE a.CustomerId = c.Id)
                  AND (
                        c.AssignedPreparerUserId IS NOT NULL
                        OR EXISTS (
                            SELECT 1 FROM TenantEmployeeDirectoryEntries d
                            WHERE d.UserId = c.CreatedByUserId
                              AND d.TenantId = c.TenantId
                              AND d.ActorType = 'TenantEmployee'
                              AND d.IsActive = 1
                              AND d.IsOffboarded = 0
                        )
                  );
                """
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: es un backfill de datos. Tras el deploy, los usuarios crean sus propias filas de
            // asignación y no hay forma fiable de distinguir las backfilleadas de las legítimas, así que
            // revertir borraría datos válidos. Para revertir en dev, limpiar CustomerAssignments a mano.
        }
    }
}
