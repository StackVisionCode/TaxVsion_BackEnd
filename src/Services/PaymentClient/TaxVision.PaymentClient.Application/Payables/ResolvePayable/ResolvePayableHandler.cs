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
        ITenantPaymentRepository payments,
        ITenantRegistry tenants,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var payable = await payables.GetByReferenceAsync(command.Reference, ct);
        if (payable is null)
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.NotFound", "No payable exists for that reference.")
            );

        // Factura anulada → payable revocado: no se acuñan links nuevos ni se redirige al checkout.
        if (payable.IsRevoked)
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.Revoked", "This invoice was voided and can no longer be paid.")
            );

        // Factura ya cobrada (InvoicePaidConsumer).
        if (payable.IsSettled)
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.AlreadyPaid", "This invoice has already been paid.")
            );

        // Segunda guarda, y la que cubre lo que la primera no puede: las facturas cobradas ANTES de
        // que existiera el consumer no tienen evento que reemitir, así que su payable sigue sin
        // liquidar. Un link en Used es prueba de cobro consumado — MarkAsUsed exige un pago asociado.
        // Solo cuenta si se usó después del último saldo de Billing: uno anterior ya está descontado (PCL-14).
        if (
            await links.AnyUsedForExternalReferenceAsync(
                payable.TenantId,
                payable.ExternalReferenceId,
                payable.AmountUpdatedAtUtc,
                ct
            )
        )
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.AlreadyPaid", "This invoice has already been paid.")
            );

        // Mientras llega InvoicePaid: un pago confirmado después del último cambio de monto ya cubrió el
        // saldo vigente. Sin cambio registrado (null) no se aplica: se cobra el saldo que mandó Billing.
        if (
            payable.AmountUpdatedAtUtc is { } amountUpdatedAtUtc
            && await payments.AnySucceededForReferenceSinceAsync(
                payable.TenantId,
                payable.ExternalReferenceId,
                amountUpdatedAtUtc,
                ct
            )
        )
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.AlreadyPaid", "This invoice has already been paid.")
            );

        // Subdominio del tenant (proyección local) para redirigir el checkout al host de la firma.
        var tenant = await tenants.GetByIdAsync(payable.TenantId, ct);
        var subDomain = tenant?.SubDomain;

        var nowUtc = DateTime.UtcNow;

        // Se reusa solo si sigue vigente y cobra el monto actual (con BIL-1 baja tras cada pago parcial).
        var existing = await links.GetActiveByExternalReferenceAsync(payable.TenantId, payable.ExternalReferenceId, ct);
        if (existing is not null && existing.IsRedeemable(nowUtc) && existing.Amount == payable.Amount)
            return Result.Success(new ResolvePayableResponse(existing.Token.Value, subDomain));

        // Un cobro sin terminar (p. ej. 3DS) sobre un link anterior: acuñar otro haría pagar dos veces.
        if (await payments.AnyInProgressForReferenceAsync(payable.TenantId, payable.ExternalReferenceId, ct))
            return Result.Failure<ResolvePayableResponse>(
                new Error("Payable.PaymentInProgress", "A payment for this invoice is already being processed.")
            );

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
        existing?.Expire(nowUtc);
        payable.RecordLinkMinted(nowUtc);
        await links.AddAsync(link, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ResolvePayableResponse(link.Token.Value, subDomain));
    }
}
