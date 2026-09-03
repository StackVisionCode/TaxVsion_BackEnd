using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Catalogs;
using TaxVision.Customer.Application.Customers.FiscalProfiles;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Infrastructure.Persistence;

public sealed class CustomerReadService(CustomerDbContext db, ISensitiveDataProtector protector) : ICustomerReadService
{
    public async Task<PagedResult<CustomerSummaryResponse>> SearchAsync(
        Guid tenantId,
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

        // Aislamiento multi-tenant: filtro explícito por el tenant del solicitante.
        // IgnoreQueryFilters() — este query corre dentro de un handler de Wolverine
        // (bus.InvokeAsync), en un scope de DI distinto al de la request HTTP que pobló
        // ITenantContext vía JwtTenantContextMiddleware; el HasQueryFilter ambiental de
        // CustomerDbContext ve Guid.Empty ahí y, ANDed con el filtro explícito de abajo,
        // garantiza 0 resultados siempre. Mismo bug/fix que project_scribe_cloudstorage_tenant_bug.
        var query = db.Customers.AsNoTracking().IgnoreQueryFilters().Where(c => c.TenantId == tenantId);

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
                c.DisplayName.ToLower().Contains(normalized)
                || c.PrimaryEmail.NormalizedValue.Contains(normalized)
                || c.PrimaryPhone != null && c.PrimaryPhone.E164Value.Contains(normalized)
                || c.Kind == CustomerKind.Individual
                    && c.BusinessIdentity != null
                    && c.BusinessIdentity.LegalName == protector.ComputeBlindIndex(normalized, tenantId)
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

    public async Task<PagedResult<CustomerReconciliationResponse>> ListForReconciliationAsync(
        CustomerStatusFilter status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (page < 1)
            page = 1;
        if (size < 1)
            size = 200;

        // Reconciliación cross-tenant: SIN filtro por tenant (a diferencia de SearchAsync). El gate de
        // autorización (solo token de PlatformTenant) vive en InternalCustomersController.Reconciliation.
        // IgnoreQueryFilters() para saltar el HasQueryFilter ambiental — igual criterio que SearchAsync.
        var query = db.Customers.AsNoTracking().IgnoreQueryFilters();

        query = status switch
        {
            CustomerStatusFilter.Active => query.Where(c => c.Status == CustomerStatus.Active),
            CustomerStatusFilter.Inactive => query.Where(c => c.Status == CustomerStatus.Inactive),
            CustomerStatusFilter.Archived => query.Where(c => c.Status == CustomerStatus.Archived),
            CustomerStatusFilter.NotArchived => query.Where(c => c.Status != CustomerStatus.Archived),
            CustomerStatusFilter.All => query,
            _ => query.Where(c => c.Status == CustomerStatus.Active),
        };

        var totalCount = await query.CountAsync(ct);

        // Orden estable por (TenantId, Id) para que la paginación sea consistente entre páginas.
        var items = await query
            .OrderBy(c => c.TenantId)
            .ThenBy(c => c.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new CustomerReconciliationResponse(
                c.TenantId,
                c.Id,
                c.DisplayName,
                c.PrimaryEmail.Value,
                c.Status
            ))
            .ToListAsync(ct);

        return new PagedResult<CustomerReconciliationResponse>(items, page, size, totalCount);
    }

    public async Task<CustomerDetailResponse?> GetDetailByIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    )
    {
        // Bloque escalar del cliente (mismo IgnoreQueryFilters + filtro explícito de tenant que SearchAsync).
        var data = await (
            from c in db.Customers.AsNoTracking().IgnoreQueryFilters()
            where c.Id == customerId && c.TenantId == tenantId
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
                FirstName = c.PersonalName != null ? c.PersonalName.FirstName : null,
                MiddleName = c.PersonalName != null ? c.PersonalName.MiddleName : null,
                LastName = c.PersonalName != null ? c.PersonalName.LastName : null,
                LegalName = c.BusinessIdentity != null ? c.BusinessIdentity.LegalName : null,
                PrimaryEmail = c.PrimaryEmail.Value,
                PrimaryPhone = c.PrimaryPhone != null ? c.PrimaryPhone.E164Value : null,
                c.Language,
                c.PreferredChannel,
                c.OccupationId,
                OccupationName = o != null ? o.Name : null,
                NaicsId = c.BusinessIdentity != null ? c.BusinessIdentity.PrincipalBusinessActivityId : null,
                NaicsDescription = naics != null ? naics.Description : null,
                c.DateOfBirth,
                c.CreatedAtUtc,
                c.AssignedPreparerUserId,
            }
        ).FirstOrDefaultAsync(ct);

        if (data is null)
            return null;

        // Sub-colecciones por queries de lectura separadas. Se proyecta a anónimos en SQL y se
        // materializa el DTO en memoria: PersonalName.DisplayName es una propiedad COMPUTADA (no
        // columna) y no traduce a SQL, y así tampoco dependemos de traducir constructores de records.
        var addressRows = await db
            .CustomerAddresses.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a => a.CustomerId == customerId && a.TenantId == tenantId)
            .OrderByDescending(a => a.IsPrimary)
            .Select(a => new
            {
                a.Id,
                a.Kind,
                a.Address.Line1,
                a.Address.Line2,
                a.Address.City,
                a.Address.Region,
                a.Address.PostalCode,
                a.Address.CountryCode,
                a.IsPrimary,
            })
            .ToListAsync(ct);

        var addresses = addressRows
            .Select(a => new AddressResponse(
                a.Id,
                a.Kind,
                a.Line1,
                a.Line2,
                a.City,
                a.Region,
                a.PostalCode,
                a.CountryCode,
                a.IsPrimary
            ))
            .ToList();

        var contactRows = await db
            .CustomerContactPoints.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(cp => cp.CustomerId == customerId && cp.TenantId == tenantId)
            .OrderByDescending(cp => cp.IsPrimary)
            .Select(cp => new
            {
                cp.Id,
                cp.Type,
                cp.Value,
                cp.Label,
                cp.IsPrimary,
                cp.VerifiedAtUtc,
            })
            .ToListAsync(ct);

        var contactPoints = contactRows
            .Select(cp => new ContactPointResponse(cp.Id, cp.Type, cp.Value, cp.Label, cp.IsPrimary, cp.VerifiedAtUtc))
            .ToList();

        var relationRows = await db
            .CustomerRelations.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(r => r.CustomerId == customerId && r.TenantId == tenantId)
            .Select(r => new
            {
                r.Id,
                r.RelationshipKind,
                r.Purposes,
                r.Name.FirstName,
                r.Name.MiddleName,
                r.Name.LastName,
                Email = r.PrimaryEmail != null ? r.PrimaryEmail.Value : null,
                Phone = r.PrimaryPhone != null ? r.PrimaryPhone.E164Value : null,
                r.DateOfBirth,
                r.IsActive,
            })
            .ToListAsync(ct);

        var relations = relationRows
            .Select(r => new RelationResponse(
                r.Id,
                r.RelationshipKind,
                r.Purposes,
                string.Join(
                    ' ',
                    new[] { r.FirstName, r.MiddleName, r.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))
                ),
                r.Email,
                r.Phone,
                r.DateOfBirth,
                r.IsActive
            ))
            .ToList();

        // Perfil fiscal SIEMPRE enmascarado: last4 + metadata, nunca el identificador completo
        // (ese sale solo por el endpoint auditado de reveal). HasRefundBankInfo se deriva del cipher.
        var fiscalRow = await db
            .CustomerFiscalProfiles.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(f => f.CustomerId == customerId && f.TenantId == tenantId)
            .Select(f => new
            {
                f.SubjectKind,
                f.TaxIdentifierLast4,
                f.FilingStatus,
                f.PriorYearAgi,
                f.IsReturningCustomer,
                HasRefundBankInfo = f.RefundBankAccountCipher != null,
                f.UpdatedAtUtc,
                f.UpdatedByUserId,
            })
            .FirstOrDefaultAsync(ct);

        var fiscalProfile = fiscalRow is null
            ? null
            : new CustomerFiscalProfileResponse(
                customerId,
                fiscalRow.SubjectKind,
                fiscalRow.TaxIdentifierLast4,
                fiscalRow.FilingStatus,
                fiscalRow.PriorYearAgi,
                fiscalRow.IsReturningCustomer,
                fiscalRow.HasRefundBankInfo,
                fiscalRow.UpdatedAtUtc,
                fiscalRow.UpdatedByUserId
            );

        return new CustomerDetailResponse(
            data.Id,
            data.TenantId,
            data.Kind,
            data.Status,
            data.DisplayName,
            data.FirstName,
            data.MiddleName,
            data.LastName,
            data.LegalName,
            data.PrimaryEmail,
            data.PrimaryPhone,
            data.Language,
            data.PreferredChannel,
            data.OccupationId,
            data.OccupationName,
            data.NaicsId,
            data.NaicsDescription,
            data.DateOfBirth,
            data.CreatedAtUtc,
            data.AssignedPreparerUserId,
            addresses,
            contactPoints,
            relations,
            fiscalProfile
        );
    }

    public async Task<IReadOnlyList<OccupationResponse>> ListOccupationsAsync(
        string? search,
        CancellationToken ct = default
    )
    {
        // Catálogo global (BaseEntity, no TenantEntity): sin filtro de tenant.
        var query = db.Occupations.AsNoTracking().Where(o => o.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(o => o.Name.Contains(term));
        }

        return await query
            .OrderBy(o => o.DisplayOrder)
            .ThenBy(o => o.Name)
            .Select(o => new OccupationResponse(o.Id, o.Name))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BusinessActivityResponse>> ListBusinessActivitiesAsync(
        string? search,
        CancellationToken ct = default
    )
    {
        var query = db.PrincipalBusinessActivities.AsNoTracking().Where(a => a.IsActive);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.NaicsCode.Contains(term) || a.Description.Contains(term));
        }

        return await query
            .OrderBy(a => a.NaicsCode)
            .Select(a => new BusinessActivityResponse(a.Id, a.NaicsCode, a.Description, a.Sector))
            .ToListAsync(ct);
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
                .IgnoreQueryFilters()
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
                    .IgnoreQueryFilters()
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
