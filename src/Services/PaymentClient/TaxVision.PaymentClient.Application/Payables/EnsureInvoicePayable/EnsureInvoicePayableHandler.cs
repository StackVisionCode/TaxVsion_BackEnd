using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.Abstractions;
using TaxVision.PaymentClient.Domain.Payables;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Payables.EnsureInvoicePayable;

/// <summary>
/// Find-or-create del <see cref="PayableReference"/> de una factura. Query-first por (tenant,
/// InvoicePayment, InvoiceId); si no existe, lo crea. Una carrera (dos ensures simultáneos del mismo
/// invoice) la resuelve el índice único: el perdedor lanza ConflictException y el retry del lado
/// Billing vuelve a entrar y encuentra al ganador. La creación del LINK con token es perezosa —
/// ocurre recién en el resolver público cuando el taxpayer abre la referencia.
/// </summary>
public static class EnsureInvoicePayableHandler
{
    public static async Task<Result<EnsureInvoicePayableResponse>> Handle(
        EnsureInvoicePayableCommand command,
        IPayableReferenceRepository payables,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<EnsureInvoicePayableResponse>(amountResult.Error);

        var existing = await payables.GetByExternalReferenceAsync(
            command.TenantId,
            PaymentPurposeKind.InvoicePayment,
            command.InvoiceId,
            ct
        );
        if (existing is not null)
            return Result.Success(new EnsureInvoicePayableResponse(existing.Id, existing.Reference));

        var created = PayableReference.Create(
            command.TenantId,
            PaymentPurposeKind.InvoicePayment,
            command.InvoiceId,
            amountResult.Value,
            DateTime.UtcNow
        );
        if (created.IsFailure)
            return Result.Failure<EnsureInvoicePayableResponse>(created.Error);

        var payable = created.Value;
        await payables.AddAsync(payable, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new EnsureInvoicePayableResponse(payable.Id, payable.Reference));
    }
}
