using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Customer.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Un cliente no puede repetirse dentro de un tenant, y la base es la que lo garantiza.
    ///
    /// <para>
    /// Va en SQL y no con <c>HasIndex</c> porque mezcla <c>TenantId</c> de la raíz con una columna del
    /// owned <c>PrimaryEmail</c>, y un índice no cruza entity types. Se comprueba en <c>sys.indexes</c>.
    /// </para>
    /// </summary>
    public partial class UniqueCustomerEmailPerTenant : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Los duplicados que ya existen impedirían crear el índice, y hacer fallar la migración
            // pararía la cadena entera de despliegue (apply-migrations.sh corre con `set -eu`).
            //
            // Se archivan los sobrantes en vez de borrarlos: archivar es un cambio de estado, se
            // revierte poniendo Active de vuelta, y el filtro del índice ya los deja fuera.
            //
            // De cada grupo se conserva UNA fila, y no la más vieja porque sí: gana la que tiene perfil
            // fiscal —el dato caro, cifrado y con su propio único— y entre iguales, la más antigua, que
            // es la que los otros servicios proyectaron primero.
            migrationBuilder.Sql(
                """
                WITH Ranked AS (
                    SELECT
                        c.Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY c.TenantId, c.PrimaryEmailNormalized
                            ORDER BY CASE WHEN f.Id IS NULL THEN 1 ELSE 0 END, c.CreatedAtUtc
                        ) AS Puesto
                    FROM Customers c
                    LEFT JOIN CustomerFiscalProfiles f ON f.CustomerId = c.Id
                    WHERE c.Status <> 'Archived'
                )
                UPDATE c
                SET c.Status = 'Archived',
                    c.ArchivedAtUtc = SYSUTCDATETIME()
                FROM Customers c
                JOIN Ranked r ON r.Id = c.Id
                WHERE r.Puesto > 1;
                """
            );

            migrationBuilder.Sql(
                """
                CREATE UNIQUE INDEX UX_Customers_Tenant_PrimaryEmailNormalized
                ON Customers (TenantId, PrimaryEmailNormalized)
                WHERE Status <> 'Archived';
                """
            );
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los archivados no se reviven: no se sabe cuáles lo estaban de antes.
            migrationBuilder.Sql("DROP INDEX UX_Customers_Tenant_PrimaryEmailNormalized ON Customers;");
        }
    }
}
