using BuildingBlocks.Results;

namespace TaxVision.PaymentApp.Application.Abstractions;

/// <summary>
/// Captura la referencia de mandato que un provider necesita para cobros off-session en
/// jurisdicciones con requisitos explícitos (SEPA en EU: Stripe <c>mandate_data</c>; PayPal
/// billing agreement). En Stripe US es no-op: el consentimiento lo guarda nuestro dominio y
/// el provider no exige referencia propia — se devuelve <see cref="MandateReference"/> con
/// <c>null</c>.
/// </summary>
public interface IMandateCollector
{
    Task<Result<MandateReference>> CollectAsync(MandateContext context, CancellationToken ct);
}

public sealed record MandateContext(
    Guid TenantId,
    string ProviderCode,
    string PayerEmail,
    string ConsentText,
    string ConsentIpAddress
);

/// <summary>
/// <paramref name="ProviderReference"/> es null cuando el provider no requiere referencia
/// explícita (Stripe US tarjeta): el consentimiento vive solo en el dominio local.
/// </summary>
public sealed record MandateReference(string? ProviderReference);
