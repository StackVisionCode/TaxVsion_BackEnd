using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Definitions.Admin;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Growth.Infrastructure.Persistence.Repositories.Codes;

/// <summary>
/// Lecturas de administración de giftcards. Solo opera con el tenant de PLATAFORMA en contexto (el PlatformAdmin):
/// cualquier otro contexto recibe vacío. Las redenciones se leen cruzando tenants, pero siempre acotadas por el Id
/// de una definición de plataforma verificada. Solo lectura, sin tracking.
/// </summary>
public sealed class CodeAdminReadModel(GrowthDbContext db, ITenantContext tenantContext) : ICodeAdminReadModel
{
    public async Task<PagedResult<CodeDefinitionDetailsResponse>> ListPlatformDefinitionsAsync(
        int page,
        int size,
        string? status,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, PlatformTenant.Id))
            return new PagedResult<CodeDefinitionDetailsResponse>([], page, size, 0);

        var query = db
            .CodeDefinitions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(definition =>
                definition.OwnerScope == CodeOwnerScope.Platform && definition.TenantId == PlatformTenant.Id
            );

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<CodeDefinitionStatus>(status, true, out var parsed))
            query = query.Where(definition => definition.Status == parsed);

        var total = await query.CountAsync(ct);
        var definitions = await query
            .Include(definition => definition.RuleVersions)
            .Include(definition => definition.Scopes)
            .OrderByDescending(definition => definition.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<CodeDefinitionDetailsResponse>(
            definitions.Select(CodeDefinitionDetailsResponse.From).ToList(),
            page,
            size,
            total
        );
    }

    public async Task<PagedResult<CodeRedemptionRow>?> ListRedemptionsAsync(
        Guid codeDefinitionId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        if (!TenantRepositoryGuard.Matches(tenantContext, PlatformTenant.Id))
            return null;

        var isPlatformCode = await db
            .CodeDefinitions.IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(
                definition =>
                    definition.Id == codeDefinitionId
                    && definition.OwnerScope == CodeOwnerScope.Platform
                    && definition.TenantId == PlatformTenant.Id,
                ct
            );
        if (!isPlatformCode)
            return null;

        var redemptions = db
            .CodeRedemptions.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(redemption => redemption.CodeDefinitionId == codeDefinitionId);

        var total = await redemptions.CountAsync(ct);
        var rows = await redemptions
            .OrderByDescending(redemption => redemption.CommittedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return new PagedResult<CodeRedemptionRow>(
            rows.Select(redemption => new CodeRedemptionRow(
                    redemption.Id,
                    redemption.CodeDefinitionId,
                    redemption.TenantId,
                    redemption.Payment.PaymentId,
                    redemption.GrossAmount.AmountCents,
                    redemption.DiscountAmount.AmountCents,
                    redemption.NetAmount.AmountCents,
                    redemption.NetAmount.Currency,
                    redemption.WasLateCommit,
                    redemption.CommittedAtUtc
                ))
                .ToList(),
            page,
            size,
            total
        );
    }
}
