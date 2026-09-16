using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Guarda en memoria lo añadido y resuelve por código/Id para los handlers de autoría de add-ons.</summary>
public sealed class FakeAddOnDefinitionRepository : IAddOnDefinitionRepository
{
    private readonly List<AddOnDefinition> _stored = [];

    public FakeAddOnDefinitionRepository(params AddOnDefinition[] seed) => _stored.AddRange(seed);

    public AddOnDefinition? Added { get; private set; }

    public Task<IReadOnlyList<AddOnDefinition>> GetPublishedAsync(CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<AddOnDefinition?> GetByCodeAsync(string code, CancellationToken ct = default) =>
        Task.FromResult(_stored.FirstOrDefault(d => d.Code.Value == code));

    public Task<AddOnDefinition?> GetByIdAsync(Guid addOnDefinitionId, CancellationToken ct = default) =>
        Task.FromResult(_stored.FirstOrDefault(d => d.Id == addOnDefinitionId));

    public Task AddAsync(AddOnDefinition definition, CancellationToken ct = default)
    {
        Added = definition;
        _stored.Add(definition);
        return Task.CompletedTask;
    }

    public Task<AddOnDefinition?> GetByIdForUpdateAsync(Guid addOnDefinitionId, CancellationToken ct = default) =>
        Task.FromResult(_stored.FirstOrDefault(d => d.Id == addOnDefinitionId));
}
