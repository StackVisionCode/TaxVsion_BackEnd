using System.Text.Json;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Team;

/// <summary>
/// Preferencia efectiva del que llama. <c>Version</c> es siempre la de su fila (0 = no existe), para que el siguiente
/// PUT la mande; <c>Payload</c> es el layout efectivo y es nulo cuando <c>Source</c> es <c>default</c>.
/// </summary>
public sealed record WorkspacePreferenceView(
    string Key,
    string Source,
    int SchemaVersion,
    long Version,
    DateTime? UpdatedAtUtc,
    JsonElement? Payload,
    OfficeDefaultView? OfficeDefault
)
{
    /// <summary>Compone la vista: fila propia con contenido, si no la plantilla con contenido, si no la de fábrica.</summary>
    public static WorkspacePreferenceView Compose(string key, WorkspacePreference? own, WorkspacePreference? office)
    {
        var effective =
            own?.PayloadJson is not null ? own
            : office?.PayloadJson is not null ? office
            : null;
        var source =
            effective is null ? WorkspacePreferenceSources.Default
            : effective == own ? WorkspacePreferenceSources.User
            : WorkspacePreferenceSources.Office;

        return new WorkspacePreferenceView(
            key,
            source,
            effective?.SchemaVersion ?? WorkspacePreferenceContent.DefaultSchemaOf(key),
            own?.Version ?? 0,
            own?.UpdatedAtUtc,
            PayloadOf(effective),
            office is null ? null : new OfficeDefaultView(office.Version, office.UpdatedAtUtc, PayloadOf(office))
        );
    }

    private static JsonElement? PayloadOf(WorkspacePreference? preference)
    {
        if (preference?.PayloadJson is null)
            return null;
        using var document = JsonDocument.Parse(preference.PayloadJson);
        return document.RootElement.Clone();
    }
}

/// <summary>Plantilla de la oficina. Viaja aunque esté vacía, para poder volver a fijarla con su versión.</summary>
public sealed record OfficeDefaultView(long Version, DateTime UpdatedAtUtc, JsonElement? Payload);

public static class WorkspacePreferenceSources
{
    public const string User = "user";
    public const string Office = "office";
    public const string Default = "default";
}
