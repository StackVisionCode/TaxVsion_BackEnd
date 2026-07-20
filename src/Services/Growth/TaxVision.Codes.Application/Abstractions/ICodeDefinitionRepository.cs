using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeDefinitionRepository
{
    /// <summary>
    /// Exact owner lookup. Implementations must not fall back to platform rows or use
    /// IgnoreQueryFilters; ownerTenantId must match CodeDefinition.TenantId.
    /// </summary>
    Task<CodeDefinition?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves a tenant-owned or platform-owned definition that is applicable to the consuming tenant.
    /// Implementations must use an explicit, audited elevation for platform-owned rows.
    /// </summary>
    Task<CodeDefinition?> GetApplicableByHashAsync(
        Guid consumingTenantId,
        CodeTokenHash codeHash,
        CancellationToken ct = default
    );

    /// <summary>
    /// Resolves a known definition while enforcing its TenantScopeId against the consuming tenant.
    /// </summary>
    Task<CodeDefinition?> GetApplicableByIdAsync(
        Guid consumingTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    );

    Task AddAsync(CodeDefinition definition, CancellationToken ct = default);
}
