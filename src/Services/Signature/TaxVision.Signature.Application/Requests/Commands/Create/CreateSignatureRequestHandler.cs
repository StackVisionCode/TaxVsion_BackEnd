using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.Create;

/// <summary>
/// Fases explícitas: (1) invocar factory del aggregate, (2) opcionalmente promover a
/// Ready si el archivo ya está disponible en CloudStorage, (3) persistir, (4) publicar
/// el evento de creación. Cada fase en un método privado con nombre autoexplicativo.
/// </summary>
public static class CreateSignatureRequestHandler
{
    public static async Task<Result<SignatureRequestResponse>> Handle(
        CreateSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        IFileMetadataRefRepository fileRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var draftResult = CreateDraft(cmd);
        if (draftResult.IsFailure)
            return Result.Failure<SignatureRequestResponse>(draftResult.Error);

        var request = draftResult.Value;
        await TryPromoteToReadyIfFileAvailable(request, cmd, fileRepository, ct);
        await PersistRequestAsync(request, repository, unitOfWork, ct);
        await PublishCreatedEventAsync(request, cmd, correlation, bus);

        return Result.Success(SignatureRequestResponse.From(request));
    }

    // ============== Fase 1: factory del aggregate ==============

    private static Result<SignatureRequest> CreateDraft(CreateSignatureRequestCommand cmd) =>
        SignatureRequest.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: cmd.Title,
            description: cmd.Description,
            category: cmd.Category,
            originalFileId: cmd.OriginalFileId,
            tokenExpirationHours: cmd.TokenExpirationHours,
            requiresSequentialSigning: cmd.RequiresSequentialSigning,
            requiresConsent: cmd.RequiresConsent,
            generateCertificate: cmd.GenerateCertificate
        );

    // ============== Fase 2: promoción opcional Draft → Ready ==============
    //
    // Si el archivo ya está disponible al momento de crear la solicitud, promovemos
    // directamente a Ready. Si aún no ha llegado FileAvailable, quedará en Draft y el
    // consumer se encargará luego.
    //
    private static async Task TryPromoteToReadyIfFileAvailable(
        SignatureRequest request,
        CreateSignatureRequestCommand cmd,
        IFileMetadataRefRepository fileRepository,
        CancellationToken ct
    )
    {
        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (file is null || file.Status != FileScanStatus.Available)
            return;

        if (string.IsNullOrEmpty(file.ChecksumSha256))
            return;

        var hashResult = Domain.Requests.ValueObjects.DocumentHash.Create(file.ChecksumSha256);
        if (hashResult.IsFailure)
            return;

        request.MarkReadyForSending(hashResult.Value);
    }

    // ============== Fase 3: persistir ==============

    private static async Task PersistRequestAsync(
        SignatureRequest request,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await repository.AddAsync(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // ============== Fase 4: publicar evento ==============

    private static Task PublishCreatedEventAsync(
        SignatureRequest request,
        CreateSignatureRequestCommand cmd,
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
                    OriginalFileId = request.OriginalFileId,
                    TokenExpirationHours = request.TokenExpirationHours,
                    RequiresSequentialSigning = request.RequiresSequentialSigning,
                    SignerCount = request.Signers.Count,
                    ExpiresAtUtc = request.ExpiresAtUtc,
                }
            )
            .AsTask();
}
