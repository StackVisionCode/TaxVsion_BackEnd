using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class EmailLayoutRepository(ScribeDbContext dbContext) : IEmailLayoutRepository
{
    public async Task<Result<EmailLayout>> GetByIdAsync(Guid layoutId, CancellationToken ct = default)
    {
        var layout = await dbContext
            .EmailLayouts.Include(l => l.Versions)
            .FirstOrDefaultAsync(l => l.Id == layoutId, ct);

        return layout is null
            ? Result.Failure<EmailLayout>(new Error("EmailLayout.NotFound", $"Layout {layoutId} was not found."))
            : Result.Success(layout);
    }

    public async Task AddAsync(EmailLayout layout, CancellationToken ct = default) =>
        await dbContext.EmailLayouts.AddAsync(layout, ct);
}
