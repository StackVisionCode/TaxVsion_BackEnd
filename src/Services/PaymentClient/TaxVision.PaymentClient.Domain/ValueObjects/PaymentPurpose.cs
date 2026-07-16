using BuildingBlocks.Results;

namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>Por qué se generó el cobro y, opcionalmente, a qué recurso externo del tenant
/// (una factura, un caso) se refiere. <see cref="ExternalReferenceId"/> es opaco — PaymentClient
/// no valida que exista, solo lo guarda para que el tenant lo reconcilie de su lado.</summary>
public sealed record PaymentPurpose
{
    public PaymentPurposeKind Kind { get; }
    public string? ExternalReferenceId { get; }

    private PaymentPurpose(PaymentPurposeKind kind, string? externalReferenceId)
    {
        Kind = kind;
        ExternalReferenceId = externalReferenceId;
    }

    public static Result<PaymentPurpose> Create(PaymentPurposeKind kind, string? externalReferenceId)
    {
        if (externalReferenceId is { Length: > 200 })
            return Result.Failure<PaymentPurpose>(new Error("PaymentPurpose.ReferenceTooLong", "ExternalReferenceId must be 200 characters or fewer."));

        return Result.Success(new PaymentPurpose(kind, externalReferenceId?.Trim()));
    }
}
