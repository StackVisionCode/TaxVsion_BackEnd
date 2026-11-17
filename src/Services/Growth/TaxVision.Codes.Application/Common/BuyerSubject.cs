using BuildingBlocks.Results;
using TaxVision.Growth.SharedKernel.Buyers;

namespace TaxVision.Codes.Application.Common;

/// <summary>
/// Del email que manda el llamador al comprador de los límites (N11). Un correo desechable no usa códigos; el email nunca
/// sale de aquí: solo el id del comprador.
/// </summary>
internal static class BuyerSubject
{
    public static Result<string?> Resolve(
        string? buyerEmail,
        IBuyerIdentityHasher hasher,
        IDisposableEmailDomains disposableDomains
    )
    {
        if (buyerEmail is null)
            return Result.Success<string?>(null);

        var email = NormalizedEmail.Create(buyerEmail);
        if (email.IsFailure)
            return Result.Failure<string?>(email.Error);

        if (disposableDomains.IsDisposable(email.Value))
            return Result.Failure<string?>(
                new Error("Codes.Buyer.DisposableEmail", "Codes cannot be used with a temporary email address.")
            );

        return Result.Success<string?>(hasher.Hash(email.Value).SubjectId);
    }
}
