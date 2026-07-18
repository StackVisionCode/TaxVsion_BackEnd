using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Rendering;

namespace TaxVision.Scribe.Application.Templates;

public sealed record PreviewTemplateVersionQuery(
    Guid VersionId,
    IReadOnlyDictionary<string, object?> SampleVariables,
    Guid? TenantId,
    bool IsPlatformAdmin
);

public sealed record PreviewTemplateResponse(string Subject, string Html, string? Text);

/// <summary>Plan §36 Fase 5, ítem 1 — renderiza una versión (Draft o Published) con variables de muestra, sin publicarla.</summary>
public static class PreviewTemplateHandler
{
    public static async Task<Result<PreviewTemplateResponse>> Handle(
        PreviewTemplateVersionQuery query,
        IEmailTemplateRepository templateRepository,
        IEmailRenderer renderer,
        CancellationToken ct
    )
    {
        var versionResult = await templateRepository.GetVersionByIdAsync(query.VersionId, ct);
        if (versionResult.IsFailure)
            return Result.Failure<PreviewTemplateResponse>(versionResult.Error);

        if (
            versionResult.Value.Template.TenantId is not null
            && versionResult.Value.Template.TenantId != query.TenantId
            && !query.IsPlatformAdmin
        )
            return Result.Failure<PreviewTemplateResponse>(
                new Error("EmailTemplateVersion.NotFound", $"Version {query.VersionId} was not found.")
            );

        var result = await renderer.PreviewAsync(query.VersionId, query.SampleVariables, ct);
        return result.IsFailure
            ? Result.Failure<PreviewTemplateResponse>(result.Error)
            : Result.Success(new PreviewTemplateResponse(result.Value.Subject, result.Value.Html, result.Value.Text));
    }
}
