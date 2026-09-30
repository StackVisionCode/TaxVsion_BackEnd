using BuildingBlocks.Results;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Common.HostedCheckout;

/// <summary>Lo que devuelve la tubería; cada tipo de checkout lo mapea a su respuesta pública.</summary>
public sealed record HostedCheckoutResult(
    Guid PaymentId,
    string CheckoutUrl,
    string ProviderSessionId,
    DateTime ExpiresAtUtc
);

/// <summary>Lo que el checkout trae del command y es igual en todos los tipos.</summary>
public sealed record HostedCheckoutRequest(
    string RequestKey,
    string PayerEmail,
    string SuccessUrl,
    string CancelUrl,
    PaymentProviderCode Provider,
    PaymentMethodKind Method
);

/// <summary>Qué hacer con un pago que ya existe bajo la misma clave de idempotencia.</summary>
public sealed record ExistingPaymentDecision
{
    private ExistingPaymentDecision(HostedCheckoutResult? response, SaaSPayment? reuse)
    {
        Response = response;
        Reuse = reuse;
    }

    /// <summary>Devolver la sesión que ya tiene, sin volver a llamar al proveedor.</summary>
    public static ExistingPaymentDecision Replay(HostedCheckoutResult response) => new(response, null);

    /// <summary>Seguir con ese mismo agregado: reintento en sitio sobre el pago existente.</summary>
    public static ExistingPaymentDecision Retry(SaaSPayment payment) => new(null, payment);

    public HostedCheckoutResult? Response { get; }
    public SaaSPayment? Reuse { get; }
}

/// <summary>
/// Lo que cambia de un checkout hosteado a otro. Todo lo demás (capabilities del proveedor, sesión de
/// 24 h, registro en el agregado, auditoría, métricas y respuesta) lo pone <see cref="HostedCheckoutPipeline"/>.
/// Cada tipo trae su propia política de idempotencia: no son intercambiables.
/// </summary>
public sealed record HostedCheckoutPolicy
{
    /// <summary>Tipo de pago; va al agregado y a las métricas.</summary>
    public required SaaSPaymentType PaymentType { get; init; }

    /// <summary>Nombre del checkout para los logs, p. ej. "Onboarding checkout".</summary>
    public required string Subject { get; init; }

    /// <summary>Lo que correlaciona el pago (onboarding o intención); solo se usa en los logs.</summary>
    public required Guid ReferenceId { get; init; }

    public required string StatementDescriptor { get; init; }

    /// <summary>De dónde sale el monto: del caller o resuelto contra Subscription.</summary>
    public required Func<CancellationToken, Task<Result<Money>>> ResolveAmount { get; init; }

    /// <summary>Crea el agregado del tipo que corresponda.</summary>
    public required Func<
        IdempotencyKey,
        Money,
        StatementDescriptor,
        DateTime,
        Result<SaaSPayment>
    > CreatePayment { get; init; }

    /// <summary>Política de idempotencia: replay, reintento en sitio o rechazo.</summary>
    public required Func<SaaSPayment, DateTime, Result<ExistingPaymentDecision>> DecideOnExisting { get; init; }

    /// <summary>Metadata que viaja al proveedor para correlacionar el webhook.</summary>
    public required Func<SaaSPayment, Dictionary<string, string>> BuildMetadata { get; init; }

    /// <summary>Cuerpo del asiento de auditoría; cada tipo nombra su propia referencia.</summary>
    public required Func<SaaSPayment, HostedCheckoutSessionResult, object> BuildAuditPayload { get; init; }

    /// <summary>Validación previa opcional (onboarding chequea su catálogo de métodos).</summary>
    public Func<CancellationToken, Task<Result>>? PreCheck { get; init; }

    /// <summary>Clave que se le manda al proveedor; por defecto la del pago.</summary>
    public Func<SaaSPayment, Result<IdempotencyKey>> ProviderKey { get; init; } =
        payment => Result.Success(payment.IdempotencyKey);
}
