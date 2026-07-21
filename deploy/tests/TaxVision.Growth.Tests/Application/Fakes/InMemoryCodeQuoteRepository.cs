using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Quotes;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeQuoteRepository : ICodeQuoteRepository
{
    private readonly List<CodeQuote> _quotes = [];

    internal IReadOnlyList<CodeQuote> Quotes => _quotes;

    internal InMemoryCodeQuoteRepository(params CodeQuote[] quotes) => _quotes.AddRange(quotes);

    public Task<CodeQuote?> GetByIdAsync(Guid tenantId, Guid quoteId, CancellationToken ct = default) =>
        Task.FromResult(_quotes.SingleOrDefault(quote => quote.Id == quoteId && quote.TenantId == tenantId));

    public Task AddAsync(CodeQuote quote, CancellationToken ct = default)
    {
        _quotes.Add(quote);
        return Task.CompletedTask;
    }
}
