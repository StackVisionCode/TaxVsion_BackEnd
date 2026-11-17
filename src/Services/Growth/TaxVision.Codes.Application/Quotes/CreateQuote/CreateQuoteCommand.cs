using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public sealed record CreateQuoteCommand(
    Guid TenantId,
    string CodeToken,
    SubjectType SubjectType,
    string SubjectId,
    string OfferOwner,
    string OfferId,
    string OfferVersion,
    long GrossAmountCents,
    string Currency,
    string SnapshotHash,
    string IdempotencyKey,
    int TtlSeconds,
    IReadOnlyCollection<CodeScopeTargetInput>? ScopeTargets = null,
    // Email del comprador (alta): los límites por sujeto cuentan por la persona. Nunca se guarda ni se loguea.
    string? BuyerEmail = null,
    // Dónde se usa el código (alta o suscripción existente) y, en una suscripción existente, la oficina compradora.
    CodeUsageContext Context = CodeUsageContext.Signup,
    Guid? BuyerTenantId = null,
    // Códigos ya aplicados en la misma compra; con ellos se valida el apilado.
    IReadOnlyCollection<Guid>? StackedWithCodeDefinitionIds = null
)
{
    public override string ToString() =>
        $"{nameof(CreateQuoteCommand)} {{ TenantId = {TenantId}, CodeToken = <redacted>, "
        + $"OfferId = {OfferId}, GrossAmountCents = {GrossAmountCents}, Currency = {Currency} }}";
}
