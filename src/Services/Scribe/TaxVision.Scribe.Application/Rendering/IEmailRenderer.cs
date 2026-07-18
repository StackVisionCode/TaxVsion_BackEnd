using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.Layouts;
using TaxVision.Scribe.Domain.Templates;

namespace TaxVision.Scribe.Application.Rendering;

public interface IEmailRenderer
{
    Task<Result<RenderedContent>> RenderAsync(RenderRequest request, CancellationToken ct = default);

    /// <summary>Renderiza una versión concreta (Draft o Published) por Id — Fase 5, sin pasar por EventKey/EventTemplateResolver.</summary>
    Task<Result<RenderedContent>> PreviewAsync(
        Guid versionId,
        IReadOnlyDictionary<string, object?> sampleVariables,
        CancellationToken ct = default
    );

    /// <summary>Parsea y cachea (L1+L2) el body/text/layout de una versión Published sin renderizar — Fase 6, usado por TemplateWarmupService al arranque.</summary>
    Task<Result> WarmupAsync(
        EmailTemplateVersion version,
        string templateKeyValue,
        EmailLayoutVersion layoutVersion,
        string layoutKeyValue,
        Guid? tenantId,
        CancellationToken ct = default
    );
}
