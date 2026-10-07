namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Los módulos (<c>module.*</c>) que incluye cada plan, en UN solo sitio.
///
/// Antes esta lista vivía dentro de <c>SubscriptionPlanCatalogSeeder</c>, que solo corre contra una
/// base vacía (<c>if (await db.Plans.AnyAsync(ct)) return;</c>). Eso bastaba para una instalación
/// nueva, pero no para una existente: cambiar el seeder no movía nada en producción. Con el catálogo
/// acá, la instalación nueva (seeder) y la existente (reconciliador de arranque) leen exactamente lo
/// mismo y no pueden divergir.
///
/// <para><b>Qué NO está y por qué.</b> <c>reports</c>, <c>marketing</c>, <c>builder</c>, <c>irs</c> y
/// <c>miles</c> se vendían en Pro/Enterprise y **no existen**: ni un endpoint los exige ni hay
/// pantalla en el CRM detrás de ellos (medido, no supuesto). Un plan que los promete cobra por algo
/// que no entrega, y con el gate de módulo aplicando tampoco protegen nada. Vuelven a la lista el día
/// que la feature exista.</para>
/// </summary>
public static class PlanModuleCatalog
{
    /// <summary>
    /// Módulos de Starter. Incluye <c>comms</c> —chat, llamadas y vídeo— a propósito: el portal del
    /// cliente existe en todos los planes, y un portal sin forma de que el cliente hable con su
    /// preparador es un portal mudo. Las reuniones van aparte (<c>meetings</c>).
    /// </summary>
    public static readonly string[] Starter = ["customers", "signatures", "documents", "planner", "comms"];

    /// <summary>Módulos de Pro.</summary>
    public static readonly string[] Pro =
    [
        "customers",
        "signatures",
        "documents",
        "planner",
        "email",
        "comms",
        "meetings",
        "campaigns",
        "wallet",
    ];

    /// <summary>
    /// Módulos de Enterprise. Coincide con Pro: al retirar los 5 módulos vacíos, lo que separa a los
    /// dos planes son las cuotas (25 vs 10 asientos, 200 vs 50 GB, 40 vs 15 invitaciones), no la lista
    /// de features. Se evaluó dejar <c>meetings</c> solo en Enterprise y se descartó: Pro ya lo tiene
    /// hoy vía <c>comms</c>, así que sería retirarle algo a quien ya lo paga, y una oficina que no
    /// puede reunirse aquí abre Zoom y se lleva con ella la interacción de más valor con su cliente.
    /// Si hace falta un diferenciador, es decisión comercial — no un dato que este catálogo invente.
    /// </summary>
    public static readonly string[] Enterprise =
    [
        "customers",
        "signatures",
        "documents",
        "planner",
        "email",
        "comms",
        "meetings",
        "campaigns",
        "wallet",
    ];

    /// <summary>Los módulos del plan, por su código. Vacío si el código no es del catálogo.</summary>
    public static IReadOnlyList<string> ForPlanCode(string planCode) =>
        planCode switch
        {
            PlanCatalog.Starter => Starter,
            PlanCatalog.Pro => Pro,
            PlanCatalog.Enterprise => Enterprise,
            _ => [],
        };

    /// <summary>Los tres planes del catálogo, con su id y sus módulos — para sembrar y para reconciliar.</summary>
    public static IReadOnlyList<(Guid PlanId, string PlanCode, IReadOnlyList<string> Modules)> All =>
        [
            (PlanCatalog.StarterId, PlanCatalog.Starter, Starter),
            (PlanCatalog.ProId, PlanCatalog.Pro, Pro),
            (PlanCatalog.EnterpriseId, PlanCatalog.Enterprise, Enterprise),
        ];
}
