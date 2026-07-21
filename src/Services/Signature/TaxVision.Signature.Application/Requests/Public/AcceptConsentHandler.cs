using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Consents;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Registra la aceptación del consent. Fases separadas por método privado — cada una
/// tiene una regla concreta:
/// <list type="number">
///   <item>Resolver token → aggregate + firmante.</item>
///   <item>Resolver el texto del consent activo (versión + hash) para la categoría/idioma.</item>
///   <item>Aplicar la mutación al aggregate (<c>AcceptSignerConsent</c>).</item>
///   <item>Insertar un <see cref="ConsentEvent"/> append-only con el snapshot exacto.</item>
///   <item>Publicar el evento de integración.</item>
/// </list>
/// </summary>
public static class AcceptConsentHandler
{
    private const string DefaultConsentLanguage = "En";

    public static async Task<Result> Handle(
        AcceptConsentCommand cmd,
        ISigningTokenService tokenService,
        ISignatureRequestRepository repository,
        IConsentTextProvider consentTextProvider,
        IConsentEventRepository consentEventRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(cmd.Token, tokenService, repository, ct);
        if (resolution.IsFailure)
            return Result.Failure(resolution.Error);

        var (request, signer) = (resolution.Value.Request, resolution.Value.Signer);
        var acceptedAt = DateTime.UtcNow;
        var acceptance = request.AcceptSignerConsent(signer.Id, acceptedAt, cmd.ClientIp, cmd.UserAgent);
        if (acceptance.IsFailure)
            return acceptance;

        var snapshot = consentTextProvider.Resolve(request.Category, DefaultConsentLanguage);
        var evt = ConsentEvent.RecordAcceptance(
            request.TenantId,
            request.Id,
            signer.Id,
            snapshot.Version,
            snapshot.Language,
            snapshot.Text,
            snapshot.TextSha256,
            cmd.ClientIp,
            cmd.UserAgent
        );
        if (evt.IsFailure)
            return Result.Failure(evt.Error);

        await consentEventRepository.AddAsync(evt.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await PublishConsentAcceptedAsync(request, signer, acceptedAt, cmd.ClientIp, correlation, bus);
        return Result.Success();
    }

    private static Task PublishConsentAcceptedAsync(
        SignatureRequest request,
        Signer signer,
        DateTime acceptedAtUtc,
        string? clientIp,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignerConsentAcceptedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    SignerId = signer.Id,
                    AcceptedAtUtc = acceptedAtUtc,
                    ClientIp = clientIp,
                }
            )
            .AsTask();
}
