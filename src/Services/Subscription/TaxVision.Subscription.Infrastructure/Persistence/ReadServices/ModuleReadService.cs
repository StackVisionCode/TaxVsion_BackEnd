using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.ReadServices;

public sealed class ModuleReadService(SubscriptionDbContext db) : IModuleReadService
{
    public async Task<List<ModuleDto>> GetAllAsync(bool? isActive, Guid? planId, CancellationToken ct = default)
    {
        var q = db.Modules.AsNoTracking().AsQueryable();
        if (isActive.HasValue)
            q = q.Where(m => m.IsActive == isActive.Value);
        if (planId.HasValue)
            q = q.Where(m => m.PlanModules.Any(pm => pm.PlanId == planId.Value));

        var flatRows = await (
            from m in q
            join pm in db.PlanModules.AsNoTracking() on m.Id equals pm.ModuleId into pmg
            from pm in pmg.DefaultIfEmpty()
            join p in db.Plans.AsNoTracking() on pm.PlanId equals p.Id into pg
            from p in pg.DefaultIfEmpty()
            select new
            {
                m.Id, m.Name, m.Description, m.Url, m.IsActive,
                PlanId   = p != null ? (Guid?)p.Id : null,
                PlanName = p != null ? p.Name      : null
            }).ToListAsync(ct);

        return flatRows.GroupBy(r => r.Id).Select(g =>
        {
            var f = g.First();
            return new ModuleDto
            {
                Id          = f.Id,
                Name        = f.Name,
                Description = f.Description,
                Url         = f.Url,
                IsActive    = f.IsActive,
                PlanIds     = g.Where(r => r.PlanId.HasValue).Select(r => r.PlanId!.Value).Distinct().ToList(),
                PlanNames   = g.Where(r => r.PlanName != null).Select(r => r.PlanName!).Distinct().ToList()
            };
        }).OrderBy(m => m.Name).ToList();
    }

    public async Task<ModuleDto> GetByIdWithDetailsAsync(Guid moduleId, CancellationToken ct = default)
    {
        var flatRows = await (
            from m in db.Modules.AsNoTracking()
            where m.Id == moduleId
            join pm in db.PlanModules.AsNoTracking() on m.Id equals pm.ModuleId into pmg
            from pm in pmg.DefaultIfEmpty()
            join p in db.Plans.AsNoTracking() on pm.PlanId equals p.Id into pg
            from p in pg.DefaultIfEmpty()
            select new
            {
                m.Id, m.Name, m.Description, m.Url, m.IsActive,
                PlanId   = p != null ? (Guid?)p.Id : null,
                PlanName = p != null ? p.Name      : null
            }).ToListAsync(ct);

        if (flatRows.Count == 0)
            throw new InvalidOperationException($"Module {moduleId} not found.");

        var first = flatRows.First();
        return new ModuleDto
        {
            Id          = first.Id,
            Name        = first.Name,
            Description = first.Description,
            Url         = first.Url,
            IsActive    = first.IsActive,
            PlanIds     = flatRows.Where(r => r.PlanId.HasValue).Select(r => r.PlanId!.Value).Distinct().ToList(),
            PlanNames   = flatRows.Where(r => r.PlanName != null).Select(r => r.PlanName!).Distinct().ToList()
        };
    }
}
