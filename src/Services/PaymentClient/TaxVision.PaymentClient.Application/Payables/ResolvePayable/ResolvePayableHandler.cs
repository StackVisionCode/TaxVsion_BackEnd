using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.PaymentLinks;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payables.ResolvePayable;

/// <summary>
/// Corazón de la URL estable: dado el token opaco público, encuentra el <see cref="PaymentLink"/>
/// Active y vigente para ese payable, o acuña uno nuevo si no hay (creación perezosa). Así el QR de
/// un PDF que vive años nunca queda muerto: cada apertura renueva el link si hace falta. Sin JWT —
/// el tenant sale del payable; los repos usan IgnoreQueryFilters + tenant explícito.
/// </summary>
public static class ResolvePayableHandler
{
    // Vigencia de cada link acuñado por el resolver (tope de dominio: ≤ 30 días).
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(7);

    // Actor M2M/sistema para CreatedBy cuando el link lo acuña el resolver (no hay usuario humano).
    private static readonly Guid ResolverActor = new("0000000b-1111-4000-8000-00000000c1ce");

    public static async Task<Result<ResolvePayableResponse>> Handle(
        ResolvePayableCommand command,
        IPayableReferenceRepository payables,
        IPaymentLinkRepository links,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var payable = await payables.GetByReferenceAsync(command.Reference, ct);
        if (payable is null)
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.NotFound", "No payable exists for that reference.")
            );

        var nowUtc = DateTime.UtcNow;

        var existing = await links.GetActiveByExternalReferenceAsync(payable.TenantId, payable.ExternalReferenceId, ct);
        if (existing is not null && existing.IsRedeemable(nowUtc))
            return Result.Success(new ResolvePayableResponse(existing.Token.Value));

        var purposeResult = PaymentPurpose.Create(payable.PurposeKind, payable.ExternalReferenceId);
        if (purposeResult.IsFailure)
            return Result.Failure<ResolvePayableResponse>(purposeResult.Error);

        var created = PaymentLink.Create(
            payable.TenantId,
            taxpayerId: null,
            payable.Amount,
            purposeResult.Value,
            PaymentLinkToken.Generate(),
            LinkLifetime,
            ResolverActor,
            nowUtc
        );
        if (created.IsFailure)
            return Result.Failure<ResolvePayableResponse>(created.Error);

        var link = created.Value;
        await links.AddAsync(link, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ResolvePayableResponse(link.Token.Value));
    }
}
