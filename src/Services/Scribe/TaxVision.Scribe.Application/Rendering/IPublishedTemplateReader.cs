using BuildingBlocks.Results;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Rendering;

/// <summary>
/// Lectura del render (SCR-09): solo la versión Published y la versión fijada del layout, sin seguimiento. Lo que
/// devuelve no se modifica; los comandos siguen usando el repositorio del agregado.
/// </summary>
public interface IPublishedTemplateReader
{
    /// <summary>
    /// El override del tenant gana sobre la plantilla del sistema, aunque no tenga versión publicada. Sin
    /// <paramref name="tenantId"/> solo se mira la del sistema.
    /// </summary>
    Task<Result<PublishedTemplate>> GetByKeyAsync(
        TemplateKey templateKey,
        Guid? tenantId,
        CancellationToken ct = default
    );

    Task<Result<PinnedLayout>> GetPinnedLayoutAsync(Guid layoutId, int versionNumber, CancellationToken ct = default);
}

/// <param name="Version">La versión Published con sus variables; null si la plantilla no tiene.</param>
public sealed record PublishedTemplate(
    Guid? TenantId,
    TemplateScope Scope,
    TemplateChannel Channel,
    string? DispatchScopeOverride,
    EmailTemplateVersion? Version
);

/// <param name="Version">La versión fijada; null si el layout no la tiene.</param>
public sealed record PinnedLayout(string LayoutKey, EmailLayoutVersion? Version);
