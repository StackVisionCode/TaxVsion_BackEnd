using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>CAT-F3 — un add-on de cuota del catálogo: suma unidades a un entitlement (ej.
/// <c>storage.max_bytes</c>) con <see cref="AddOnMergeStrategy"/>. A diferencia de los add-ons de
/// módulo, no enciende módulos y respeta <see cref="MutexGroup"/>:
/// dos SKUs del mismo grupo no pueden coexistir activos en el mismo tenant.</summary>
public sealed record QuotaAddOnDefinition(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string EntitlementKey,
    EntitlementValueType ValueType,
    string EntitlementAmount,
    AddOnMergeStrategy MergeStrategy,
    string MutexGroup,
    decimal MonthlyUsd,
    AddOnAvailability Availability
)
{
    public bool Offered => Availability == AddOnAvailability.Offered;
}

/// <summary>Catálogo de add-ons de cuota sembrado y reconciliado al iniciar.
/// Fase 3 introduce los storage packs; add-ons de cuota futuros (<c>+N invitaciones</c>, <c>+M MB</c>)
/// pueden agregarse acá o crearse por admin API genérica (<c>POST admin/subscription/addons/quota</c>).
///
/// <para><b>Tamaños y precios</b> acordados: Small +2 GB/$3, Medium +5 GB/$6, Large +10 GB/$10. Yearly
/// = Monthly × 10 (igual criterio que módulos). Los tres comparten <c>MutexGroup = storage</c>: un
/// tenant tiene UN tamaño activo, no acumula los tres.</para>
/// </summary>
public static class QuotaAddOnCatalog
{
    /// <summary>Clave canónica del grupo mutex de storage. También usada por fitness.</summary>
    public const string StorageMutexGroup = "storage";

    public static readonly QuotaAddOnDefinition[] All =
    [
        new(
            new Guid("d2000000-0000-0000-0000-000000000001"),
            "addon.storage.small",
            "Storage +2 GB",
            "Añade 2 GB al límite de almacenamiento del tenant.",
            EntitlementKey: "storage.max_bytes",
            ValueType: EntitlementValueType.Long,
            // 2 * 1024^3 = 2 GiB en bytes
            EntitlementAmount: "2147483648",
            MergeStrategy: AddOnMergeStrategy.Sum,
            MutexGroup: StorageMutexGroup,
            MonthlyUsd: 3m,
            Availability: AddOnAvailability.Offered
        ),
        new(
            new Guid("d2000000-0000-0000-0000-000000000002"),
            "addon.storage.medium",
            "Storage +5 GB",
            "Añade 5 GB al límite de almacenamiento del tenant.",
            EntitlementKey: "storage.max_bytes",
            ValueType: EntitlementValueType.Long,
            // 5 * 1024^3
            EntitlementAmount: "5368709120",
            MergeStrategy: AddOnMergeStrategy.Sum,
            MutexGroup: StorageMutexGroup,
            MonthlyUsd: 6m,
            Availability: AddOnAvailability.Offered
        ),
        new(
            new Guid("d2000000-0000-0000-0000-000000000003"),
            "addon.storage.large",
            "Storage +10 GB",
            "Añade 10 GB al límite de almacenamiento del tenant.",
            EntitlementKey: "storage.max_bytes",
            ValueType: EntitlementValueType.Long,
            // 10 * 1024^3
            EntitlementAmount: "10737418240",
            MergeStrategy: AddOnMergeStrategy.Sum,
            MutexGroup: StorageMutexGroup,
            MonthlyUsd: 10m,
            Availability: AddOnAvailability.Offered
        ),
    ];
}
