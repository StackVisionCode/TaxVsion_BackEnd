namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <param name="CanViewBilling">
/// Si el caller tiene <c>billing.view</c>. Este endpoint lo llama el shell del CRM para el banner de ciclo
/// de vida, así que lo consume CUALQUIER empleado; sin el permiso se responde el MISMO contrato con los
/// campos comerciales en cero/nulos, no un 403, para no apagarle el banner en silencio al staff.
/// </param>
public sealed record GetMySubscriptionQuery(Guid TenantId, bool CanViewBilling = true);
