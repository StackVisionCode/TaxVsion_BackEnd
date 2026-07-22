using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryCodeDefinitionRepository : ICodeDefinitionRepository
{
    private readonly List<CodeDefinition> _definitions = [];

    internal IReadOnlyList<CodeDefinition> Definitions => _definitions;
    internal CodeTokenHash? LastApplicableHash { get; private set; }

    internal InMemoryCodeDefinitionRepository(params CodeDefinition[] definitions) =>
        _definitions.AddRange(definitions);

    public Task<CodeDefinition?> GetOwnedByIdAsync(
        Guid ownerTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _definitions.SingleOrDefault(definition =>
                definition.Id == codeDefinitionId && definition.TenantId == ownerTenantId
            )
        );

    public Task<CodeDefinition?> GetApplicableByHashAsync(
        Guid consumingTenantId,
        CodeTokenHash codeHash,
        CancellationToken ct = default
    )
    {
        LastApplicableHash = codeHash;
        return Task.FromResult(
            _definitions.SingleOrDefault(definition =>
                definition.CodeHash == codeHash
                && (definition.TenantScopeId is null || definition.TenantScopeId == consumingTenantId)
            )
        );
    }

    public Task<CodeDefinition?> GetApplicableByIdAsync(
        Guid consumingTenantId,
        Guid codeDefinitionId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _definitions.SingleOrDefault(definition =>
                definition.Id == codeDefinitionId
                && (definition.TenantScopeId is null || definition.TenantScopeId == consumingTenantId)
            )
        );

    public Task<CodeDefinition?> GetActiveBenefitGiftByTenantScopeAsync(
        Guid tenantScopeId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _definitions
                .Where(definition =>
                    definition.TenantScopeId == tenantScopeId
                    && definition.Kind == CodeKind.BenefitGift
                    && definition.Status == CodeDefinitionStatus.Active
                )
                .OrderByDescending(definition => definition.CreatedAtUtc)
                .FirstOrDefault()
        );

    public Task AddAsync(CodeDefinition definition, CancellationToken ct = default)
    {
        _definitions.Add(definition);
        return Task.CompletedTask;
    }
}
