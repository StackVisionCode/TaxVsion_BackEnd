using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.ProviderCustomers.Commands.CreateSetupIntent;

/// <summary>Crea (aprovisionando el customer si hace falta) un SetupIntent en el provider para que
/// el frontend recolecte una tarjeta con la UI de Stripe (Payment Element) sin que el PAN toque el
/// backend. Devuelve el <c>client_secret</c> que el front usa para confirmar.</summary>
public sealed record CreateSetupIntentCommand(Guid TenantId, PaymentProviderCode Provider);

public sealed record SetupIntentResponse(string ClientSecret);
