namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <param name="CanViewBilling">
/// Si el caller tiene <c>billing.view</c>. A5 (A6.4 del plan) — este endpoint lo llama el shell del CRM
/// para el banner de ciclo de vida, así que lo consume CUALQUIER empleado; hasta ahora eso le mostraba
/// también el precio del plan, el nombre comercial, los límites y el último fallo de cobro.
/// <para>
/// Sin el permiso se responde el MISMO contrato con los campos comerciales en cero/nulos, no un 403: el
/// CRM desplegado hoy pide este endpoint en cada sesión y solo lee <c>status</c>,
/// <c>billingAccessBlocked</c> y <c>gracePeriodEndsAtUtc</c>. Un 403 le apagaría el banner en silencio a
/// todos los empleados (§R.7 del plan: ningún chequeo nuevo puede quitar acceso que hoy funciona). Lo
/// mínimo y limpio para el banner es <c>GET subscriptions/me/status</c>, que es adonde el CRM migra.
/// </para>
/// </param>
public sealed record GetMySubscriptionQuery(Guid TenantId, bool CanViewBilling = true);
