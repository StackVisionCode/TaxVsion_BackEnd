using TaxVision.Codes.Domain.Quotes;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeQuoteRepository
{
    Task<CodeQuote?> GetByIdAsync(Guid tenantId, Guid quoteId, CancellationToken ct = default);

    Task AddAsync(CodeQuote quote, CancellationToken ct = default);
}
