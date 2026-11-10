using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.TenantIntegrationEvents;

/// <summary>
/// Publicado por Tenant al guardar el perfil público de la oficina. Estado completo (no delta): cada
/// proyección copia solo lo que usa. Nunca lleva datos de cumplimiento (EFIN, responsable de
/// seguridad, WISP). <see cref="Version"/> crece en cada cambio: un consumidor ignora una versión
/// menor o igual a la que ya tiene. Todos los campos de texto son opcionales (null = sin dato).
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.TenantIntegrationEvents.TenantProfileChangedIntegrationEvent")]
public sealed record TenantProfileChangedIntegrationEvent : IntegrationEvent
{
    public required Guid ProfileTenantId { get; init; }
    public required long Version { get; init; }
    public required DateTime UpdatedAtUtc { get; init; }

    public string? LegalName { get; init; }

    // Nombre comercial (DBA). Lo que ve el cliente (correos, remitente, certificados, checkout) es
    // DisplayName ?? nombre de la oficina; la factura usa LegalName ?? DisplayName ?? nombre de la oficina.
    public string? DisplayName { get; init; }
    public string? EntityType { get; init; }
    public string? Ein { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Website { get; init; }
    public OfficeAddressData? PhysicalAddress { get; init; }

    // null = la postal es la misma que la física.
    public OfficeAddressData? MailingAddress { get; init; }

    // "en" | "es"; null = inglés.
    public string? DefaultLanguage { get; init; }
    public IReadOnlyList<OfficeBusinessHoursData> BusinessHours { get; init; } = [];

    // Feriados y días no laborables (aditivo): vacío en eventos anteriores.
    public IReadOnlyList<OfficeClosureData> Closures { get; init; } = [];
    public string? BookingUrl { get; init; }
    public string? ReviewUrl { get; init; }

    /// <summary>Dirección a la que se le escribe a la oficina (pie de correos, "Billed to"): la postal si
    /// la tiene, si no la física. Método y no propiedad para no viajar duplicada en el payload.</summary>
    public OfficeAddressData? ResolvePostalAddress() => MailingAddress ?? PhysicalAddress;
}

public sealed record OfficeAddressData(
    string? Line1,
    string? Line2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country
)
{
    /// <summary>Una línea: "1600 Main St, Suite 2, Miami, FL 33101" (el país solo si no es US). Null si
    /// falta la calle o la ciudad: una dirección a medias no sirve para escribirle a nadie.</summary>
    public string? ToSingleLine()
    {
        if (string.IsNullOrWhiteSpace(Line1) || string.IsNullOrWhiteSpace(City))
            return null;

        var region = string.Join(
            ' ',
            new[] { State, PostalCode }.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim())
        );
        var country =
            string.IsNullOrWhiteSpace(Country)
            || string.Equals(Country.Trim(), "US", StringComparison.OrdinalIgnoreCase)
                ? null
                : Country.Trim();
        return string.Join(
            ", ",
            new[] { Line1, Line2, City, region, country }
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p!.Trim())
        );
    }
}

/// <summary>
/// Un cierre de la oficina: fecha <c>yyyy-MM-dd</c>, fin opcional (incluido) y si se repite cada año el
/// mismo día. Fechas del calendario de la oficina, sin hora.
/// </summary>
public sealed record OfficeClosureData(string Date, string? EndDate, string Label, bool RepeatsYearly);

/// <summary>Día ISO (Monday…Sunday) con apertura y cierre "HH:mm" en la zona horaria de la oficina.</summary>
public sealed record OfficeBusinessHoursData(string Day, string Opens, string Closes);
