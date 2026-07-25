using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Quotes;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

public sealed class CodeQuoteRepository(GrowthDbContext dbContext, ITenantContext tenantContext) : ICodeQuoteRepository
{
    public Task<CodeQuote?> GetByIdAsync(Guid tenantId, Guid quoteId, CancellationToken ct = default) =>
        !TenantRepositoryGuard.Matches(tenantContext, tenantId) || quoteId == Guid.Empty
            ? Task.FromResult<CodeQuote?>(null)
            : dbContext
                .CodeQuotes.IgnoreQueryFilters()
                .FirstOrDefaultAsync(quote => quote.Id == quoteId && quote.TenantId == tenantId, ct);

    public async Task AddAsync(CodeQuote quote, CancellationToken ct = default)
    {
        TenantRepositoryGuard.EnsureMatches(tenantContext, quote.TenantId);
        await dbContext.CodeQuotes.AddAsync(quote, ct);
    }
}
