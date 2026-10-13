using BuildingBlocks.Messaging.PlatformIntegrationEvents;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Domain.Platform;

namespace TaxVision.Tenant.Application.Platform;

/// <summary>
/// Marca y dominio de la plataforma. Es público a propósito (nombre comercial, dominio y correos de
/// contacto ya aparecen en cada correo): lo leen los demás servicios al arrancar y los frontends.
/// </summary>
public sealed record PlatformSettingsResponse(
    long Version,
    string BrandName,
    string LegalName,
    string BaseDomain,
    string ApiHost,
    string AppHost,
    string ClientHost,
    string Website,
    string SupportEmail,
    string BillingEmail,
    DateTime UpdatedAtUtc
);

internal static class PlatformSettingsMapping
{
    public static PlatformSettingsResponse ToResponse(this PlatformSettings settings) =>
        new(
            settings.Version,
            settings.BrandName,
            settings.LegalName,
            settings.BaseDomain,
            settings.ApiHost,
            $"app.{settings.BaseDomain}",
            $"client.{settings.BaseDomain}",
            $"https://{settings.BaseDomain}",
            settings.SupportEmail,
            settings.BillingEmail,
            settings.UpdatedAtUtc
        );

    public static PlatformSettingsChangedIntegrationEvent ToChangedEvent(
        this PlatformSettings settings,
        string correlationId
    ) =>
        new()
        {
            TenantId = PlatformTenant.Id,
            Version = settings.Version,
            BrandName = settings.BrandName,
            LegalName = settings.LegalName,
            BaseDomain = settings.BaseDomain,
            ApiHost = settings.ApiHost,
            SupportEmail = settings.SupportEmail,
            BillingEmail = settings.BillingEmail,
            ChangedAtUtc = settings.UpdatedAtUtc,
            CorrelationId = correlationId,
        };

    public static BuildingBlocks.Results.Error NotFound { get; } =
        new("PlatformSettings.NotFound", "Platform settings have not been initialized yet.");
}
