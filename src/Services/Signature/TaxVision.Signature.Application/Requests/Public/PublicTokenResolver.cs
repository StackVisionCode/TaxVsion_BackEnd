using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Public;

/// <summary>
/// Resuelve un token público a un par <c>(request, signer)</c> validando:
/// <list type="number">
///   <item>Firma RS256 y expiración (<see cref="ISigningTokenService.Verify"/>).</item>
///   <item>Existencia del <c>SignatureRequest</c> para el <c>TenantId</c> del payload.</item>
///   <item><c>RevocationEpoch</c> igual al del aggregate (invalidación global de tokens).</item>
///   <item>Existencia del <c>Signer</c> referenciado.</item>
///   <item><c>jti</c> no revocado explícitamente (<see cref="IJtiDenylist"/>).</item>
/// </list>
/// Diseñado para reutilizarse desde los handlers públicos.
/// </summary>
internal static class PublicTokenResolver
{
    internal sealed record ResolvedContext(SignatureRequest Request, Signer Signer);

    public static async Task<Result<ResolvedContext>> ResolveAsync(
        string token,
        ISigningTokenService tokenService,
        ISignatureRequestRepository requestRepository,
        CancellationToken ct
    ) => await ResolveAsync(token, tokenService, requestRepository, denylist: null, ct);

    public static async Task<Result<ResolvedContext>> ResolveAsync(
        string token,
        ISigningTokenService tokenService,
        ISignatureRequestRepository requestRepository,
        IJtiDenylist? denylist,
        CancellationToken ct
    )
    {
        var verification = tokenService.Verify(token);
        if (verification.IsFailure)
            return Result.Failure<ResolvedContext>(verification.Error);

        var payload = verification.Value;
        if (denylist is not null && await denylist.IsRevokedAsync(payload.TokenId, ct))
            return Result.Failure<ResolvedContext>(new Error("Signature.Token.Revoked", "This link has been revoked."));

        var request = await requestRepository.GetByIdAsync(payload.TenantId, payload.SignatureRequestId, ct);
        if (request is null)
            return Result.Failure<ResolvedContext>(
                new Error("Signature.Request.NotFound", "The signature request does not exist.")
            );

        if (payload.RevocationEpoch != request.RevocationEpoch)
            return Result.Failure<ResolvedContext>(new Error("Signature.Token.Revoked", "This link has been revoked."));

        var signer = request.Signers.FirstOrDefault(s => s.Id == payload.SignerId);
        if (signer is null)
            return Result.Failure<ResolvedContext>(
                new Error("Signature.Signer.NotFound", "Signer not found on the request.")
            );

        return Result.Success(new ResolvedContext(request, signer));
    }
}
