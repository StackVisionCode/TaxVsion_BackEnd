using TaxVision.PaymentClient.Domain.PaymentLinks;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

public sealed record SearchPaymentLinksQuery(Guid TenantId, PaymentLinkStatus? Status, int Page, int PageSize);
