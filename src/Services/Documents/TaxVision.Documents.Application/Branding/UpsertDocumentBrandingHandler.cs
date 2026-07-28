using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;

namespace TaxVision.Documents.Application.Branding;

/// <summary>Crea o actualiza el perfil de marca del tenant (uno por tenant). Se invoca desde el endpoint
/// admin (M2M con el tenant del JWT). La validación de apariencia vive en el aggregate.</summary>
public static class UpsertDocumentBrandingHandler
{
    public static async Task<Result<DocumentBrandingDto>> Handle(
        UpsertDocumentBrandingCommand command,
        IDocumentBrandingRepository repository,
        IUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<UpsertDocumentBrandingCommand> logger,
        CancellationToken ct
    )
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var existing = await repository.GetByTenantAsync(command.TenantId, ct);

        if (existing is null)
        {
            var created = DocumentBranding.Create(
                command.TenantId,
                command.DisplayName,
                command.LogoDataUri,
                command.BrandColorHex,
                command.FooterText,
                now
            );
            if (created.IsFailure)
                return Result.Failure<DocumentBrandingDto>(created.Error);

            await repository.AddAsync(created.Value, ct);
            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation("Document branding created for tenant {TenantId}.", command.TenantId);
            return Result.Success(ToDto(created.Value));
        }

        var updated = existing.Update(
            command.DisplayName,
            command.LogoDataUri,
            command.BrandColorHex,
            command.FooterText,
            now
        );
        if (updated.IsFailure)
            return Result.Failure<DocumentBrandingDto>(updated.Error);

        await unitOfWork.SaveChangesAsync(ct);
        logger.LogInformation("Document branding updated for tenant {TenantId}.", command.TenantId);
        return Result.Success(ToDto(existing));
    }

    internal static DocumentBrandingDto ToDto(DocumentBranding b) =>
        new(b.DisplayName, b.LogoDataUri, b.BrandColorHex, b.FooterText, b.UpdatedAtUtc);
}

/// <summary>Devuelve el perfil de marca del tenant (o null si no configuró ninguno).</summary>
public static class GetDocumentBrandingHandler
{
    public static async Task<Result<DocumentBrandingDto?>> Handle(
        GetDocumentBrandingQuery query,
        IDocumentBrandingRepository repository,
        CancellationToken ct
    )
    {
        var branding = await repository.GetByTenantAsync(query.TenantId, ct);
        return Result.Success(branding is null ? null : UpsertDocumentBrandingHandler.ToDto(branding));
    }
}
