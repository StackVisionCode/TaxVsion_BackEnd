namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>
/// Por qué un add-on está o no en la tienda. Es un enum y no un booleano porque las dos razones para
/// retirarlo son distintas y tratarlas igual confunde: una es un producto que no existe y la otra un
/// producto que existe y ya no hace falta vender.
/// </summary>
public enum AddOnAvailability
{
    /// <summary>En la tienda: un plan inferior puede comprarlo suelto.</summary>
    Offered,

    /// <summary>
    /// No existe: ni endpoint que lo exija ni pantalla en el producto. Se vendía igual. Vuelve a la
    /// tienda el día que la feature exista.
    /// </summary>
    NotBuilt,

    /// <summary>
    /// Existe y funciona, pero su módulo pasó a estar incluido en TODOS los planes, así que no hay a
    /// quién vendérselo: el guard <c>AddOn.AlreadyIncludedInPlan</c> lo rechazaría siempre.
    /// </summary>
    IncludedInEveryPlan,
}

/// <summary>Un add-on de módulo del catálogo: lo que se siembra y lo que se reconcilia.</summary>
/// <param name="Availability">
/// Distinto de <see cref="AddOnAvailability.Offered"/> = fuera de la tienda. El reconciliador de
/// arranque lo pasa a <c>Deprecated</c>, con lo que desaparece (<c>GetPublishedAsync</c> filtra por
/// <c>Published</c>) y nadie puede comprarlo. Se usa <c>Deprecated</c> y no <c>Archived</c> a
/// propósito: consigue exactamente el mismo efecto y deja el estado terminal para después —
/// <c>Archive</c> exige pasar por <c>Deprecated</c> y ninguno de los dos tiene vuelta atrás en la API
/// de dominio de hoy.
/// </param>
public sealed record ModuleAddOnDefinition(
    Guid Id,
    string Code,
    string Name,
    string Module,
    decimal MonthlyUsd,
    AddOnAvailability Availability
)
{
    public bool Offered => Availability == AddOnAvailability.Offered;
}

/// <summary>
/// Catálogo de add-ons de módulo, en UN solo sitio: lo leen el seeder (instalación nueva) y el
/// reconciliador de arranque (instalación existente), que antes no podían coincidir porque el seeder
/// solo corre contra una base vacía.
///
/// <para><b>Los que no se ofrecen</b> se retiran por dos razones distintas, y conviene no
/// confundirlas:</para>
/// <list type="bullet">
/// <item><c>reports</c>, <c>marketing</c>, <c>builder</c>, <c>irs</c>, <c>miles</c>: **no existen**.
/// Ni endpoint que los exija ni pantalla en el CRM. Se vendían a 29-49 USD/mes con renovación
/// automática, o sea pagar por nada. Medido antes de retirarlos: ningún tenant había comprado
/// ninguno.</item>
/// <item><c>comms</c>: existe y funciona, pero pasó a estar incluido en TODOS los planes, así que ya
/// no hay a quién vendérselo — el guard <c>AddOn.AlreadyIncludedInPlan</c> lo rechazaría siempre.</item>
/// </list>
/// </summary>
public static class ModuleAddOnCatalog
{
    public static readonly ModuleAddOnDefinition[] All =
    [
        new(
            new Guid("d1000000-0000-0000-0000-000000000001"),
            "addon-email",
            "Correo",
            "email",
            29m,
            Availability: AddOnAvailability.Offered
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000009"),
            "addon-meetings",
            "Reuniones",
            "meetings",
            29m,
            Availability: AddOnAvailability.Offered
        ),
        // `comms` (chat, llamadas y vídeo) pasó a estar en TODOS los planes, así que este add-on se
        // quedó sin comprador posible: el guard `AddOn.AlreadyIncludedInPlan` lo rechazaría a
        // cualquiera. No es que no exista — existe y funciona; es que ya no hay a quién vendérselo.
        new(
            new Guid("d1000000-0000-0000-0000-000000000002"),
            "addon-comms",
            "Comunicacion",
            "comms",
            29m,
            Availability: AddOnAvailability.IncludedInEveryPlan
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000003"),
            "addon-campaigns",
            "Campanas",
            "campaigns",
            29m,
            Availability: AddOnAvailability.Offered
        ),
        // ---- Sin nada construido detrás: no se ofrecen ----
        new(
            new Guid("d1000000-0000-0000-0000-000000000004"),
            "addon-reports",
            "Reportes",
            "reports",
            29m,
            Availability: AddOnAvailability.NotBuilt
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000005"),
            "addon-marketing",
            "Marketing",
            "marketing",
            49m,
            Availability: AddOnAvailability.NotBuilt
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000006"),
            "addon-builder",
            "Builder",
            "builder",
            49m,
            Availability: AddOnAvailability.NotBuilt
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000007"),
            "addon-irs",
            "IRS",
            "irs",
            49m,
            AddOnAvailability.NotBuilt
        ),
        new(
            new Guid("d1000000-0000-0000-0000-000000000008"),
            "addon-miles",
            "Millas",
            "miles",
            49m,
            AddOnAvailability.NotBuilt
        ),
    ];

    /// <summary>Los que el reconciliador debe retirar de la tienda si siguen publicados.</summary>
    public static IEnumerable<ModuleAddOnDefinition> NotOffered => All.Where(addOn => !addOn.Offered);

    /// <summary>Los que se retiran porque no existen — los que vuelven cuando la feature se construya.</summary>
    public static IEnumerable<ModuleAddOnDefinition> NotBuilt =>
        All.Where(addOn => addOn.Availability == AddOnAvailability.NotBuilt);
}
