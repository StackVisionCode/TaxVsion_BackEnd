using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaxVision.Subscription.Infrastructure.Persistence.Migrations
{
    /// <summary>CAT-F5.3 — El enum <c>PlanTier.Standard</c> se renombró a <c>Starter</c>; como la
    /// columna <c>SubscriptionPlans.Tier</c> es <c>HasConversion&lt;string&gt;</c>, las filas que
    /// quedaron con el string "Standard" tras despliegues previos deben pasarse a "Starter" o un
    /// <c>Enum.Parse&lt;PlanTier&gt;("Standard")</c> explotaría al leer el plan.
    ///
    /// En local la DB estaba vacía al scaffoldear (0 filas afectadas, verificado por SQL), así que
    /// el UPDATE no cambia nada; pero una instalación con planes sembrados (prod/cert) sí requiere
    /// este UPDATE antes del primer boot del binario nuevo. Down revierte por si hay rollback.
    /// </summary>
    public partial class RenamePlanTierStandardToStarter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [SubscriptionPlans] SET [Tier] = N'Starter' WHERE [Tier] = N'Standard';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE [SubscriptionPlans] SET [Tier] = N'Standard' WHERE [Tier] = N'Starter';");
        }
    }
}
