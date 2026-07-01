using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.ReadServices;

public sealed class PlanReadService(SubscriptionDbContext db) : IPlanReadService
{
    public async Task<List<PlanDto>> GetAllAsync(bool? isActive, CancellationToken ct = default)
    {
        var flatRows = await (
            from p in db.Plans.AsNoTracking()
            where isActive == null || p.IsActive == isActive
            join pf in db.PlanFeatures.AsNoTracking() on p.Id equals pf.PlanId into pfg
            from pf in pfg.DefaultIfEmpty()
            join pm in db.PlanModules.AsNoTracking() on p.Id equals pm.PlanId into pmg
            from pm in pmg.DefaultIfEmpty()
            join m in db.Modules.AsNoTracking() on pm.ModuleId equals m.Id into mg
            from m in mg.DefaultIfEmpty()
            select new
            {
                p.Id, p.Name, p.Title, p.Description, p.BasePriceMonthly,
                p.BasePriceAnnual, p.PricePerAdditionalSeat, p.IncludedSeats,
                p.Currency, p.IsActive, p.ServiceLevel, p.UpdatedAtUtc,
                FeatureCode = pf != null ? pf.FeatureCode : null,
                ModuleId    = m  != null ? (Guid?)m.Id    : null,
                ModuleName  = m  != null ? m.Name         : null
            }).ToListAsync(ct);

        return flatRows.GroupBy(r => r.Id).Select(g =>
        {
            var f = g.First();
            return new PlanDto
            {
                Id                   = f.Id,
                Name                 = f.Name,
                Title                = f.Title,
                Description          = f.Description,
                BasePriceMonthly     = f.BasePriceMonthly,
                BasePriceAnnual      = f.BasePriceAnnual,
                PricePerAdditionalSeat = f.PricePerAdditionalSeat,
                IncludedSeats        = f.IncludedSeats,
                Currency             = f.Currency,
                IsActive             = f.IsActive,
                ServiceLevel         = f.ServiceLevel,
                UpdatedAtUtc         = f.UpdatedAtUtc,
                Features   = g.Where(r => r.FeatureCode != null).Select(r => r.FeatureCode!).Distinct().ToList(),
                ModuleIds  = g.Where(r => r.ModuleId.HasValue).Select(r => r.ModuleId!.Value).Distinct().ToList(),
                ModuleNames = g.Where(r => r.ModuleName != null).Select(r => r.ModuleName!).Distinct().ToList()
            };
        }).OrderBy(p => p.Name).ToList();
    }

    public async Task<PlanDto> GetByIdWithDetailsAsync(Guid planId, CancellationToken ct = default)
    {
        var flatRows = await (
            from p in db.Plans.AsNoTracking()
            where p.Id == planId
            join pf in db.PlanFeatures.AsNoTracking() on p.Id equals pf.PlanId into pfg
            from pf in pfg.DefaultIfEmpty()
            join pm in db.PlanModules.AsNoTracking() on p.Id equals pm.PlanId into pmg
            from pm in pmg.DefaultIfEmpty()
            join m in db.Modules.AsNoTracking() on pm.ModuleId equals m.Id into mg
            from m in mg.DefaultIfEmpty()
            select new
            {
                p.Id, p.Name, p.Title, p.Description, p.BasePriceMonthly,
                p.BasePriceAnnual, p.PricePerAdditionalSeat, p.IncludedSeats,
                p.Currency, p.IsActive, p.ServiceLevel, p.UpdatedAtUtc,
                FeatureCode = pf != null ? pf.FeatureCode : null,
                ModuleId    = m  != null ? (Guid?)m.Id    : null,
                ModuleName  = m  != null ? m.Name         : null
            }).ToListAsync(ct);

        if (flatRows.Count == 0)
            throw new InvalidOperationException($"Plan {planId} not found.");

        var first = flatRows.First();
        return new PlanDto
        {
            Id                   = first.Id,
            Name                 = first.Name,
            Title                = first.Title,
            Description          = first.Description,
            BasePriceMonthly     = first.BasePriceMonthly,
            BasePriceAnnual      = first.BasePriceAnnual,
            PricePerAdditionalSeat = first.PricePerAdditionalSeat,
            IncludedSeats        = first.IncludedSeats,
            Currency             = first.Currency,
            IsActive             = first.IsActive,
            ServiceLevel         = first.ServiceLevel,
            UpdatedAtUtc         = first.UpdatedAtUtc,
            Features   = flatRows.Where(r => r.FeatureCode != null).Select(r => r.FeatureCode!).Distinct().ToList(),
            ModuleIds  = flatRows.Where(r => r.ModuleId.HasValue).Select(r => r.ModuleId!.Value).Distinct().ToList(),
            ModuleNames = flatRows.Where(r => r.ModuleName != null).Select(r => r.ModuleName!).Distinct().ToList()
        };
    }
}
