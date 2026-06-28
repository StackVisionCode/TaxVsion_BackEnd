using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Infrastructure.Persistence;

public sealed class CustomerReadService(CustomerDbContext db) : ICustomerReadService
{
    public async Task<IReadOnlyList<CustomerSummaryResponse>> SearchAsync(
        string? term,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.Customers.AsNoTracking().Where(c => c.Status != CustomerStatus.Archived);

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = term.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.DisplayName.ToLower().Contains(normalized) || c.PrimaryEmail.NormalizedValue.Contains(normalized)
            );
        }

        return await query
            .OrderBy(c => c.DisplayName)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new CustomerSummaryResponse(
                c.Id,
                c.Kind,
                c.Status,
                c.DisplayName,
                c.PrimaryEmail.Value,
                c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null,
                c.CreatedAtUtc
            ))
            .ToListAsync(ct);
    }

    public async Task<CustomerResponse?> GetByIdAsync(Guid customerId, CancellationToken ct = default)
    {
        var data = await (
            from c in db.Customers.AsNoTracking()
            where c.Id == customerId
            from o in db.Occupations.Where(x => x.Id == c.OccupationId).DefaultIfEmpty()
            from naics in db
                .PrincipalBusinessActivities.Where(x =>
                    c.BusinessIdentity != null && x.Id == c.BusinessIdentity.PrincipalBusinessActivityId
                )
                .DefaultIfEmpty()
            select new
            {
                c.Id,
                c.TenantId,
                c.Kind,
                c.Status,
                c.DisplayName,
                PrimaryEmail = c.PrimaryEmail.Value,
                PrimaryPhone = c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null,
                c.Language,
                c.PreferredChannel,
                c.OccupationId,
                OccupationName = o != null ? o.Name : null,
                NaicsId = c.BusinessIdentity != null ? c.BusinessIdentity.PrincipalBusinessActivityId : null,
                NaicsDescription = naics != null ? naics.Description : null,
                c.CreatedAtUtc,
            }
        ).FirstOrDefaultAsync(ct);

        if (data is null)
            return null;

        return new CustomerResponse(
            data.Id,
            data.TenantId,
            data.Kind,
            data.Status,
            data.DisplayName,
            data.PrimaryEmail,
            data.PrimaryPhone,
            data.Language,
            data.PreferredChannel,
            data.OccupationId,
            data.OccupationName,
            data.NaicsId,
            data.NaicsDescription,
            data.CreatedAtUtc
        );
    }
}
