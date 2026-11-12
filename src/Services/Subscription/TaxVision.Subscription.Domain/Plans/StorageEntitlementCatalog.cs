using System.Globalization;
using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Claves <c>storage.*</c> de archivos por plan (además de <c>storage.max_bytes</c>, la cuota, que ya existía).
/// Las lee CloudStorage del snapshot de entitlements. Los valores replican lo que hoy estaba fijo en su
/// <c>appsettings</c> por nombre de plan (PlanPolicies): desplegar no cambia lo que cada plan puede subir.
/// Un plan vende GRUPOS de tipos de archivo, no extensiones; las extensiones de cada grupo las mantiene la
/// plataforma en CloudStorage.
/// </summary>
public static class StorageEntitlementCatalog
{
    public const string MaxFileBytes = "storage.max_file_bytes";
    public const string FileTypeGroups = "storage.file_type_groups";

    /// <summary>Grupos de tipos de archivo conocidos (separados por coma en el valor del entitlement).</summary>
    public static class Groups
    {
        public const string Documents = "documents";
        public const string Spreadsheets = "spreadsheets";
        public const string Images = "images";
        public const string Data = "data";
        public const string Media = "media";
        public const string Presentations = "presentations";
        public const string RichText = "rich_text";
        public const string WebImages = "web_images";
        public const string Archives = "archives";
        public const string Xml = "xml";
    }

    public static IReadOnlyList<string> Keys { get; } = [MaxFileBytes, FileTypeGroups];

    private static readonly string[] BaseGroups =
    [
        Groups.Documents,
        Groups.Spreadsheets,
        Groups.Images,
        Groups.Data,
        Groups.Media,
    ];

    private static readonly string[] ExtendedGroups =
    [
        .. BaseGroups,
        Groups.Presentations,
        Groups.RichText,
        Groups.WebImages,
        Groups.Archives,
        Groups.Xml,
    ];

    /// <summary>Un solo valor por clave, el de Enterprise: 25 MB y todos los grupos (D-F2: nunca perjudica).</summary>
    public static IReadOnlyCollection<EntitlementSeed> Defaults { get; } =
    [
        new(MaxFileBytes, EntitlementValueType.Long, (25L * 1024 * 1024).ToString(CultureInfo.InvariantCulture)),
        new(FileTypeGroups, EntitlementValueType.String, string.Join(',', ExtendedGroups)),
    ];
}
