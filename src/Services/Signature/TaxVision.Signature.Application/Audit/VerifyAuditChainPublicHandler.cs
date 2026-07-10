using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Requests.Public;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Application.Audit;

/// <summary>
/// Verifica públicamente la cadena de audit de una solicitud, autorizado por el mismo
/// token del firmante. Fases: (1) resolver token → aggregate, (2) leer la cadena
/// completa, (3) delegar la verificación al servicio, (4) empaquetar la respuesta con
/// los eventos + el veredicto. Los payloads se exponen tal cual porque son eventos
/// audit; no contienen tokens ni credenciales.
/// </summary>
public static class VerifyAuditChainPublicHandler
{
    public static async Task<Result<AuditChainVerificationResponse>> Handle(
        VerifyAuditChainPublicQuery query,
        ISigningTokenService tokenService,
        ISignatureRequestRepository requestRepository,
        ISignatureAuditRepository auditRepository,
        IAuditChainVerifier verifier,
        CancellationToken ct
    )
    {
        var resolution = await PublicTokenResolver.ResolveAsync(query.Token, tokenService, requestRepository, ct);
        if (resolution.IsFailure)
            return Result.Failure<AuditChainVerificationResponse>(resolution.Error);

        var request = resolution.Value.Request;
        var events = await auditRepository.ListAsync(request.TenantId, request.Id, ct);
        var verification = await verifier.VerifyAsync(request.TenantId, request.Id, events, ct);

        return Result.Success(
            new AuditChainVerificationResponse(
                SignatureRequestId: request.Id,
                IsIntact: verification.IsIntact,
                EventCount: verification.EventCount,
                LastSequence: verification.LastSequence,
                Defect: verification.Defect,
                Events: events.Select(MapView).ToList()
            )
        );
    }

    private static AuditChainEventView MapView(SignatureAuditEvent evt) =>
        new(evt.Sequence, evt.Kind, evt.OccurredAtUtc, evt.PayloadJson, evt.ChainHash);
}
