namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

/// <summary>Lista todas las configs de pago del tenant (activas e inactivas) para la pantalla de
/// settings — el tenant gestiona qué métodos ofrece.</summary>
public sealed record ListTenantPaymentConfigsQuery(Guid TenantId);
