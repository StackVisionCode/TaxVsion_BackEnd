using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Infrastructure.Persistence;

public sealed class CustomerReadService(CustomerDbContext db, ISensitiveDataProtector protector) : ICustomerReadService
{
    public async Task<PagedResult<CustomerSummaryResponse>> SearchAsync(
        string? term,
        CustomerStatusFilter status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (size < 1)
            size = 20;

        var query = db.Customers.AsNoTracking().AsQueryable();

        query = status switch
        {
            CustomerStatusFilter.Active => query.Where(c => c.Status == CustomerStatus.Active),
            CustomerStatusFilter.Inactive => query.Where(c => c.Status == CustomerStatus.Inactive),
            CustomerStatusFilter.Archived => query.Where(c => c.Status == CustomerStatus.Archived),
            CustomerStatusFilter.NotArchived => query.Where(c => c.Status != CustomerStatus.Archived),
            CustomerStatusFilter.All => query,
            _ => query.Where(c => c.Status == CustomerStatus.Active),
        };

        if (!string.IsNullOrWhiteSpace(term))
        {
            var normalized = term.Trim().ToLowerInvariant();
            query = query.Where(c =>
                c.DisplayName.ToLower().Contains(normalized) || c.PrimaryEmail.NormalizedValue.Contains(normalized)
            );
        }

        var totalCount = await query.CountAsync(ct);

        var items = await query
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

        return new PagedResult<CustomerSummaryResponse>(items, page, size, totalCount);
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

    public async Task<CustomerExistsResponse> CheckExistsAsync(
        Guid tenantId,
        string? email,
        string? taxIdentifier,
        CancellationToken ct = default
    )
    {
        Guid? matchId = null;
        var emailExists = false;
        var taxIdExists = false;

        if (!string.IsNullOrWhiteSpace(email))
        {
            var normalized = email.Trim().ToLowerInvariant();
            var hit = await db
                .Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId && c.PrimaryEmail.NormalizedValue == normalized)
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct);
            emailExists = hit.HasValue;
            matchId ??= hit;
        }

        if (!string.IsNullOrWhiteSpace(taxIdentifier))
        {
            var normalizedDigits = new string(taxIdentifier.Where(char.IsDigit).ToArray());
            if (normalizedDigits.Length == 9)
            {
                var blindIndex = protector.ComputeBlindIndex(normalizedDigits, tenantId);
                var hit = await db
                    .CustomerFiscalProfiles.AsNoTracking()
                    .Where(fp => fp.TenantId == tenantId && fp.TaxIdentifierBlindIndex == blindIndex)
                    .Select(fp => (Guid?)fp.CustomerId)
                    .FirstOrDefaultAsync(ct);
                taxIdExists = hit.HasValue;
                matchId ??= hit;
            }
        }

        return new CustomerExistsResponse(emailExists, taxIdExists, matchId);
    }
}
