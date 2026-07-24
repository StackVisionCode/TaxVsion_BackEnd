using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class EmailLayoutRepository(ScribeDbContext dbContext) : IEmailLayoutRepository
{
    // IgnoreQueryFilters: mismo patrón que EmailTemplateRepository.GetByIdAsync — llamado desde
    // handlers de Wolverine (bus.InvokeAsync) donde el ITenantContext ambiente puede llegar
    // vacío, y EmailLayout es INullableTenantOwned (System-or-Tenant). Todos los llamadores
    // (Add/Publish EmailLayoutVersion) validan post-fetch vía AuthorizeWrite(layout.TenantId,
    // command.TenantId, ...) — el filtro ambiental era redundante con esa guarda.
    public async Task<Result<EmailLayout>> GetByIdAsync(Guid layoutId, CancellationToken ct = default)
    {
        var layout = await dbContext
            .EmailLayouts.IgnoreQueryFilters()
            .Include(l => l.Versions)
            .FirstOrDefaultAsync(l => l.Id == layoutId, ct);

        return layout is null
            ? Result.Failure<EmailLayout>(new Error("EmailLayout.NotFound", $"Layout {layoutId} was not found."))
            : Result.Success(layout);
    }

    public async Task AddAsync(EmailLayout layout, CancellationToken ct = default) =>
        await dbContext.EmailLayouts.AddAsync(layout, ct);
}
