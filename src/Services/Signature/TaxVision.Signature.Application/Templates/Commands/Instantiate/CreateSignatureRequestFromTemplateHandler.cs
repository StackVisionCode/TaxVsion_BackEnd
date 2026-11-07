using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Documents.Verification;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Templates;
using Wolverine;

namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Instancia una plantilla publicada:
/// <list type="number">
///   <item>Cargar plantilla y validar estado <c>Published</c>.</item>
///   <item>Armar la solicitud con <see cref="SignatureRequestFromTemplateBuilder"/>.</item>
///   <item>Promover a Ready si el archivo ya está disponible (misma lógica de <see cref="Requests.Commands.Create.CreateSignatureRequestHandler"/>).</item>
///   <item>Persistir y publicar <c>SignatureRequestCreated</c>.</item>
/// </list>
/// </summary>
public static class CreateSignatureRequestFromTemplateHandler
{
    public static async Task<Result<SignatureRequestResponse>> Handle(
        CreateSignatureRequestFromTemplateCommand cmd,
        ISignatureTemplateRepository templateRepository,
        ISignatureRequestRepository requestRepository,
        SignatureRequestFromTemplateBuilder builder,
        IRequestDocumentVerifier verifier,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var template = await templateRepository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Failure("Signature.Template.NotFound", "The signature template does not exist for this tenant.");
        if (template.Status != SignatureTemplateStatus.Published)
            return Failure("Signature.Template.NotPublished", "Only published templates can be instantiated.");

        var built = await builder.BuildAsync(cmd, template, ct);
        if (built.IsFailure)
            return Result.Failure<SignatureRequestResponse>(built.Error);

        var request = built.Value;

        var verified = await VerifyAvailableFiles(request, verifier, ct);
        if (verified.IsFailure)
            return Result.Failure<SignatureRequestResponse>(verified.Error);

        await requestRepository.AddAsync(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        await PublishCreatedEventAsync(request, template, correlation, bus);

        return Result.Success(SignatureRequestResponse.From(request));
    }

    // Revisa en el servidor los archivos ya disponibles; uno inválido corta la creación.
    private static async Task<Result> VerifyAvailableFiles(
        SignatureRequest request,
        IRequestDocumentVerifier verifier,
        CancellationToken ct
    )
    {
        foreach (var document in request.Documents.ToList())
        {
            var verification = await verifier.VerifyAsync(request, document.Id, ct);
            if (verification.Kind == DocumentVerificationKind.Rejected)
                return Result.Failure(DocumentVerificationErrors.Rejected(document, verification.Rejection!));
        }
        return Result.Success();
    }

    private static Task PublishCreatedEventAsync(
        SignatureRequest request,
        SignatureTemplate template,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestCreatedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    Title = request.Title,
                    Category = request.Category.ToString(),
                    OriginalFileIds = request.Documents.Select(document => document.OriginalFileId).ToList(),
                    DocumentCount = request.Documents.Count,
                    TokenExpirationHours = request.TokenExpirationHours,
                    RequiresSequentialSigning = request.RequiresSequentialSigning,
                    SignerCount = request.Signers.Count,
                    ExpiresAtUtc = request.ExpiresAtUtc,
                }
            )
            .AsTask();

    private static Result<SignatureRequestResponse> Failure(string code, string message) =>
        Result.Failure<SignatureRequestResponse>(new Error(code, message));
}
